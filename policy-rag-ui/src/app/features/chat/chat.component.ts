import { Component, inject, signal, effect, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ChatService } from '../../core/services/chat.service';
import { ChatMessage, SourceReference } from '../../core/models/chat.model';

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './chat.component.html',
  styleUrls: ['./chat.component.scss']
})
export class ChatComponent implements AfterViewChecked {
  private chatService = inject(ChatService);

  @ViewChild('messageContainer') messageContainer?: ElementRef;

  messages = signal<ChatMessage[]>([]);
  currentStreamedResponse = signal('');
  isLoading = signal(false);
  selectedDepartment = signal<string | undefined>(undefined);
  userInput = signal('');

  private shouldScrollToBottom = false;

  departments = [
    'All Departments',
    'Human Resources',
    'IT',
    'Finance',
    'Legal',
    'Operations'
  ];

  constructor() {
    // Auto-scroll when new messages are added
    effect(() => {
      if (this.messages().length > 0 || this.currentStreamedResponse()) {
        this.shouldScrollToBottom = true;
      }
    });
  }

  ngAfterViewChecked(): void {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
      this.shouldScrollToBottom = false;
    }
  }

  sendMessage(): void {
    const content = this.userInput().trim();
    if (!content || this.isLoading()) return;

    this.isLoading.set(true);
    this.currentStreamedResponse.set('');

    // Add user message
    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      content,
      role: 'user',
      timestamp: new Date()
    };
    this.messages.update(msgs => [...msgs, userMessage]);
    this.userInput.set('');

    // Add placeholder for assistant message
    const assistantMessageId = crypto.randomUUID();
    const assistantMessage: ChatMessage = {
      id: assistantMessageId,
      content: '',
      role: 'assistant',
      timestamp: new Date(),
      isStreaming: true
    };
    this.messages.update(msgs => [...msgs, assistantMessage]);

    // Stream the response
    const department = this.selectedDepartment() === 'All Departments'
      ? undefined
      : this.selectedDepartment();

    this.chatService.streamMessage(content, department).subscribe({
      next: (chunk: string) => {
        this.currentStreamedResponse.update(current => current + chunk);

        // Update the assistant message with streamed content
        this.messages.update(msgs =>
          msgs.map(msg =>
            msg.id === assistantMessageId
              ? { ...msg, content: this.currentStreamedResponse() }
              : msg
          )
        );
      },
      complete: () => {
        // Mark streaming as complete
        this.messages.update(msgs =>
          msgs.map(msg =>
            msg.id === assistantMessageId
              ? { ...msg, isStreaming: false }
              : msg
          )
        );
        this.currentStreamedResponse.set('');
        this.isLoading.set(false);
      },
      error: (error: any) => {
        console.error('Chat error:', error);
        this.messages.update(msgs =>
          msgs.map(msg =>
            msg.id === assistantMessageId
              ? {
                  ...msg,
                  content: 'Sorry, I encountered an error processing your request.',
                  isStreaming: false
                }
              : msg
          )
        );
        this.currentStreamedResponse.set('');
        this.isLoading.set(false);
      }
    });
  }

  onKeyPress(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.sendMessage();
    }
  }

  clearChat(): void {
    this.messages.set([]);
    this.currentStreamedResponse.set('');
  }

  private scrollToBottom(): void {
    try {
      if (this.messageContainer) {
        this.messageContainer.nativeElement.scrollTop =
          this.messageContainer.nativeElement.scrollHeight;
      }
    } catch (err) {
      console.error('Scroll error:', err);
    }
  }
}
