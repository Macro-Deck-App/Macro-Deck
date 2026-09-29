export interface Announcement {
  number: number;
  title: string;
  content: string;
  publishedAt: string;
  updatedAt: string;
}

export interface GetPendingAnnouncementResponse {
  announcement: Announcement | null;
}

export interface MarkAnnouncementSeenRequest {
  number: number;
}

export interface AnnouncementChangedEvent {
  announcement: Announcement | null;
}
