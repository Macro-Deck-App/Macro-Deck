import type { ResultResponse } from './common';
import type { FontFaceSlant } from './system';

export type UserFontFormat = 'ttf' | 'otf';

export interface UserFont {
  fontId: string;
  faceId: string;
  family: string;
  styleName: string;
  weight: number;
  width: number;
  slant: FontFaceSlant;
  format: UserFontFormat;
  sizeBytes: number;
}

export interface GetUserFontsResponse {
  fonts: UserFont[];
}

export type UserFontImportStatus =
  | 'Imported'
  | 'AlreadyPresent'
  | 'UnsupportedFormat'
  | 'TooLarge'
  | 'InvalidFont'
  | 'AlreadyInstalled'
  | 'AlreadyImported';

export interface UserFontImportResult {
  fileName: string;
  status: UserFontImportStatus;
  font?: UserFont | null;
}

export interface ImportUserFontsResponse extends ResultResponse {
  results: UserFontImportResult[];
}

export interface DeleteUserFontResponse {
  success: boolean;
}
