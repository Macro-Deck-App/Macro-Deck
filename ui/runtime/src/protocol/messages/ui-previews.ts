export interface UiPreviewEntry {
  id: string;
  view: string;
  scenario: string;
  profile: string;
  ownerId: string;
}

export interface UiPreviewDiagnostic {
  member: string;
  reason: string;
}

export interface ListUiPreviewsRequest {}

export interface ListUiPreviewsResponse {
  previews: UiPreviewEntry[];
  diagnostics: UiPreviewDiagnostic[];
}
