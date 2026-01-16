import { Routes } from '@angular/router';
import { ChatComponent } from './features/chat/chat.component';
import { DocumentListComponent } from './features/documents/document-list/document-list.component';
import { DocumentUploadComponent } from './features/documents/document-upload/document-upload.component';

export const routes: Routes = [
  { path: '', redirectTo: '/chat', pathMatch: 'full' },
  { path: 'chat', component: ChatComponent },
  { path: 'documents', component: DocumentListComponent },
  { path: 'upload', component: DocumentUploadComponent }
];
