import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DocumentService } from '../../../core/services/document.service';
import { Document } from '../../../core/models/document.model';

@Component({
  selector: 'app-document-list',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './document-list.component.html',
  styleUrls: ['./document-list.component.scss']
})
export class DocumentListComponent implements OnInit {
  private documentService = inject(DocumentService);

  documents = signal<Document[]>([]);
  isLoading = signal(false);
  currentPage = signal(1);
  pageSize = signal(20);
  totalCount = signal(0);
  deletingDocumentId = signal<string | null>(null);

  // Expose Math to template
  protected readonly Math = Math;

  ngOnInit(): void {
    this.loadDocuments();
  }

  loadDocuments(): void {
    this.isLoading.set(true);

    this.documentService.getDocuments(this.currentPage(), this.pageSize()).subscribe({
      next: (response) => {
        this.documents.set(response.documents);
        this.totalCount.set(response.totalCount);
        this.isLoading.set(false);
      },
      error: (error) => {
        console.error('Error loading documents:', error);
        this.isLoading.set(false);
        alert('Failed to load documents. Please try again.');
      }
    });
  }

  deleteDocument(id: string, fileName: string): void {
    if (!confirm(`Are you sure you want to delete "${fileName}"? This action cannot be undone.`)) {
      return;
    }

    this.deletingDocumentId.set(id);

    this.documentService.deleteDocument(id).subscribe({
      next: () => {
        this.documents.update(docs => docs.filter(d => d.id !== id));
        this.totalCount.update(count => count - 1);
        this.deletingDocumentId.set(null);
      },
      error: (error) => {
        console.error('Error deleting document:', error);
        this.deletingDocumentId.set(null);
        alert('Failed to delete document. Please try again.');
      }
    });
  }

  reprocessDocument(id: string): void {
    if (!confirm('This will regenerate embeddings for the document. Continue?')) {
      return;
    }

    this.documentService.reprocessDocument(id).subscribe({
      next: () => {
        alert('Document reprocessing started. It may take a few minutes.');
        this.loadDocuments();
      },
      error: (error) => {
        console.error('Error reprocessing document:', error);
        alert('Failed to reprocess document. Please try again.');
      }
    });
  }

  nextPage(): void {
    if (this.hasNextPage()) {
      this.currentPage.update(page => page + 1);
      this.loadDocuments();
    }
  }

  previousPage(): void {
    if (this.hasPreviousPage()) {
      this.currentPage.update(page => page - 1);
      this.loadDocuments();
    }
  }

  hasNextPage(): boolean {
    return this.currentPage() * this.pageSize() < this.totalCount();
  }

  hasPreviousPage(): boolean {
    return this.currentPage() > 1;
  }

  formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
  }

  getStatusClass(status: string): string {
    return `status-${status}`;
  }

  getStatusLabel(status: string): string {
    const labels: { [key: string]: string } = {
      'processing': 'Processing',
      'completed': 'Ready',
      'failed': 'Failed'
    };
    return labels[status] || status;
  }
}
