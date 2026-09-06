export interface GetVersionRequest {}

export interface GetVersionResponse {
  version: string;
  isBeta: boolean;
}

export interface GetServerTimeResponse {
  utcMs: number;
}
