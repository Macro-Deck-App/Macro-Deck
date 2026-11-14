export interface GetHostSessionResponse {
  sessionId: string;
  restoreApplied: boolean;
  pluginsDisabledAtStartup?: boolean;
}
