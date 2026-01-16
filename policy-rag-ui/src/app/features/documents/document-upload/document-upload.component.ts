import { Component, inject, signal, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DocumentService } from '../../../core/services/document.service';
import { DocumentUploadResult } from '../../../core/models/document.model';

@Component({
  selector: 'app-document-upload',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './document-upload.component.html',
  styleUrls: ['./document-upload.component.scss']
})
export class DocumentUploadComponent {
  private documentService = inject(DocumentService);

  isDragOver = signal(false);
  uploadProgress = signal(0);
  uploadResults = signal<DocumentUploadResult[]>([]);
  isUploading = signal(false);
  uploadComplete = output<void>();

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);

    const files = Array.from(event.dataTransfer?.files || []);
    this.uploadFiles(files);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files || []);
    this.uploadFiles(files);
    input.value = ''; // Reset input
  }

  private uploadFiles(files: File[]): void {
    if (files.length === 0) return;

    // Validate file types
    const allowedTypes = ['.pdf', '.docx', '.txt'];
    const invalidFiles = files.filter(f =>
      !allowedTypes.some(type => f.name.toLowerCase().endsWith(type))
    );

    if (invalidFiles.length > 0) {
      alert(`Invalid file types detected. Only PDF, DOCX, and TXT files are allowed.`);
      return;
    }

    this.isUploading.set(true);
    this.uploadProgress.set(0);
    this.uploadResults.set([]);

    this.documentService.uploadDocuments(files).subscribe({
      next: (progress) => {
        this.uploadProgress.set(progress.progress);
        if (progress.results) {
          this.uploadResults.set(progress.results);
        }
      },
      complete: () => {
        this.isUploading.set(false);
        this.uploadComplete.emit();

        // Reset after a delay
        setTimeout(() => {
          this.uploadProgress.set(0);
          this.uploadResults.set([]);
        }, 3000);
      },
      error: (error) => {
        console.error('Upload error:', error);
        this.isUploading.set(false);
        alert('An error occurred during upload. Please try again.');
      }
    });
  }

  clearResults(): void {
    this.uploadResults.set([]);
    this.uploadProgress.set(0);
  }
}
