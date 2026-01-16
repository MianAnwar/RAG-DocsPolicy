export interface Document {
  id: string;
  fileName: string;
  uploadDate: Date;
  fileSize: number;
  pageCount: number;
  chunkCount: number;
  status: 'processing' | 'completed' | 'failed';
  department?: string;
  errorMessage?: string;
}

export interface DocumentUploadResult {
  fileName: string;
  success: boolean;
  documentId?: string;
  error?: string;
  chunkCount?: number;
}

export interface UploadProgress {
  progress: number;
  results?: DocumentUploadResult[];
}

export interface DocumentListResponse {
  documents: Document[];
  totalCount: number;
}
