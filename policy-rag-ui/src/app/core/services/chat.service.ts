import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ChatResponse, ChatRequest } from '../models/chat.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ChatService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/chat`;

  /**
   * Send a chat message and get a complete response
   */
  sendMessage(message: string, departmentFilter?: string): Observable<ChatResponse> {
    const request: ChatRequest = {
      message,
      departmentFilter
    };
    return this.http.post<ChatResponse>(this.apiUrl, request);
  }

  /**
   * Stream a chat message response using Server-Sent Events
   */
  streamMessage(message: string, departmentFilter?: string): Observable<string> {
    return new Observable(observer => {
      const params = new URLSearchParams();
      params.append('message', message);
      if (departmentFilter) {
        params.append('department', departmentFilter);
      }

      const eventSource = new EventSource(
        `${this.apiUrl}/stream?${params.toString()}`
      );

      eventSource.addEventListener('message', (event: MessageEvent) => {
        try {
          const data = JSON.parse(event.data);
          if (data.content) {
            observer.next(data.content);
          }
        } catch (error) {
          console.error('Error parsing SSE message:', error);
        }
      });

      eventSource.addEventListener('done', () => {
        eventSource.close();
        observer.complete();
      });

      eventSource.addEventListener('error', (error) => {
        console.error('SSE Error:', error);
        eventSource.close();
        observer.error(error);
      });

      // Cleanup function
      return () => {
        eventSource.close();
      };
    });
  }
}
