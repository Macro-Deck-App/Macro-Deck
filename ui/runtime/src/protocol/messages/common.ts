export interface ApiError {
  code: string;
  message: string;
}

export interface ResultResponse {
  success: boolean;
  error?: ApiError;
}

export interface Result<T = void> {
  success: boolean;
  error?: ApiError;
  data?: T;
}
