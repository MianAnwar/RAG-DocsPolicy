using Microsoft.AspNetCore.Mvc;
using PolicyRAG.Api.Models;
using PolicyRAG.Api.Models.Requests;
using PolicyRAG.Api.Models.Responses;
using PolicyRAG.Api.Services;

namespace PolicyRAG.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly RetrievalService _retrievalService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(
        RetrievalService retrievalService,
        ILogger<SearchController> logger)
    {
        _retrievalService = retrievalService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SearchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation("Processing search request: {Query}", request.Query);

        try
        {
            var retrievalOptions = new RetrievalOptions
            {
                TopK = request.TopK ?? 10,
                ScoreThreshold = request.ScoreThreshold ?? 0.5f,
                DepartmentFilter = request.DepartmentFilter
            };

            var results = await _retrievalService.RetrieveContextAsync(request.Query, retrievalOptions);

            _logger.LogInformation(
                "Search completed: found {ResultCount} results for query",
                results.Count);

            return Ok(new SearchResponse
            {
                Results = results,
                TotalCount = results.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing search request");
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An error occurred while processing your search. Please try again.");
        }
    }
}
