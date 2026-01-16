export interface ChatMessage {
  id: string;
  content: string;
  role: 'user' | 'assistant';
  timestamp: Date;
  sources?: SourceReference[];
  isStreaming?: boolean;
}

export interface SourceReference {
  index: number;
  documentTitle: string;
  pageNumber: number;
  content: string;
  score: number;
  section?: string;
}

export interface ChatResponse {
  answer: string;
  sources: SourceReference[];
}

export interface ChatRequest {
  message: string;
  departmentFilter?: string;
}
