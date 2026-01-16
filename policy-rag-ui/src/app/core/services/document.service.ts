import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpEventType } from '@angular/common/http';
import { Observable, filter, map } from 'rxjs';
import { Document, DocumentUploadResult, UploadProgress, DocumentListResponse } from '../models/document.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class DocumentService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/documents`;

  /**
   * Upload documents with progress tracking
   */
  uploadDocuments(files: File[]): Observable<UploadProgress> {
    const formData = new FormData();
    files.forEach(file => {
      formData.append('files', file, file.name);
    });

    return this.http.post<DocumentUploadResult[]>(`${this.apiUrl}/upload`, formData, {
      reportProgress: true,
      observe: 'events'
    }).pipe(
      map(event => {
        if (event.type === HttpEventType.UploadProgress && event.total) {
          const progress = Math.round((100 * event.loaded) / event.total);
          return { progress };
        } else if (event.type === HttpEventType.Response) {
          return {
            progress: 100,
            results: event.body || []
          };
        }
        return { progress: 0 };
      }),
      filter(progress => progress.progress !== 0)
    );
  }

  /**
   * Get paginated list of documents
   */
  getDocuments(page = 1, pageSize = 20): Observable<DocumentListResponse> {
    return this.http.get<DocumentListResponse>(this.apiUrl, {
      params: { page: page.toString(), pageSize: pageSize.toString() }
    });
  }

  /**
   * Get a single document by ID
   */
  getDocument(id: string): Observable<Document> {
    return this.http.get<Document>(`${this.apiUrl}/${id}`);
  }

  /**
   * Delete a document
   */
  deleteDocument(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  /**
   * Reprocess a document (regenerate embeddings)
   */
  reprocessDocument(id: string): Observable<void> {
    return this.http.post<void>(`${this.apiUrl}/${id}/reprocess`, {});
  }
}
