import { ResultResponse } from './common';

export type IconProcessingState = 'Pending' | 'Processing' | 'Ready' | 'Failed';

export type IconImportBatchState =
  | 'Discovering'
  | 'Processing'
  | 'Completed'
  | 'CompletedWithErrors'
  | 'Failed'
  | 'Cancelled';

export interface IpcIconPack {
  id: string;
  name: string;
  description?: string;
  author?: string;
  version?: string;
  isDefault: boolean;
  isReadOnly: boolean;
  sourceType: string;
  createdAt: string;
  updatedAt: string;
  iconCount: number;
  ownerKind?: string;
  canDelete?: boolean;
}

export interface IpcIcon {
  id: string;
  packId: string;
  name: string;
  width?: number;
  height?: number;
  isAnimated: boolean;
  processingState: IconProcessingState;
  processingError?: string;
  availableSizes: number[];
  originalFileName?: string;
  createdAt: string;
}

export interface IpcIconImportBatch {
  id: string;
  packId: string;
  state: IconImportBatchState;
  sourceName?: string;
  total?: number;
  processed: number;
  failed: number;
  error?: string;
}

export interface GetIconPacksRequest {}

export interface GetIconPacksResponse {
  packs: IpcIconPack[];
}

export interface GetIconsRequest {
  packId: string;
}

export interface GetIconsResponse {
  icons: IpcIcon[];
}

export interface CreateIconPackRequest {
  name: string;
  description?: string;
  author?: string;
  version?: string;
}

export interface CreateIconPackResponse extends ResultResponse {
  pack?: IpcIconPack;
}

export interface UpdateIconPackRequest {
  name: string;
  description?: string;
  author?: string;
  version?: string;
}

export interface UpdateIconPackResponse extends ResultResponse {
  pack?: IpcIconPack;
}

export interface DeleteIconPackResponse extends ResultResponse {}

export interface UpdateIconRequest {
  name: string;
}

export interface UpdateIconResponse extends ResultResponse {
  icon?: IpcIcon;
}

export interface DeleteIconResponse extends ResultResponse {}

export interface DeleteIconsRequest {
  ids: string[];
}

export interface DeleteIconsResponse extends ResultResponse {
  deletedCount: number;
}

export interface ImportIconsFromPathRequest {
  paths: string[];
}

export interface ImportSingleIconFromPathRequest {
  packId?: string;
  path: string;
}

export interface ImportSingleIconResponse extends ResultResponse {
  icon?: IpcIcon;
  reused?: boolean;
}

export interface ImportIconsResponse extends ResultResponse {
  batch?: IpcIconImportBatch;
}

export interface ImportIconPacksResponse extends ResultResponse {
  batch?: IpcIconImportBatch;
  packs?: IpcIconPack[];
}

export interface GetIconImportBatchResponse {
  batch?: IpcIconImportBatch;
}

export interface CancelIconImportBatchResponse {
  cancelled: boolean;
}

export interface IconPackCreatedEvent {
  pack: IpcIconPack;
}

export interface IconPackUpdatedEvent {
  pack: IpcIconPack;
}

export interface IconPackDeletedEvent {
  packId: string;
}

export interface IconsAddedEvent {
  batchId?: string;
  packId: string;
  icons: IpcIcon[];
}

export interface IconUpdatedEvent {
  icon: IpcIcon;
}

export interface IconDeletedEvent {
  iconId: string;
  packId: string;
}

export interface IconImportProgressEvent {
  batchId: string;
  packId: string;
  state: IconImportBatchState;
  sourceName?: string;
  total?: number;
  processed: number;
  failed: number;
  error?: string;
}
