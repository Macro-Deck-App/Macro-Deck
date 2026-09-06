import { LocalizedText } from '../../localization/localized-text';

export interface IpcFolderView {
  id: string;
  providerId: string;
  name?: LocalizedText;
  description?: LocalizedText;
  navigation: string;
  hasConfiguration: boolean;
  isBuiltIn: boolean;
}

export interface GetFolderViewsRequest {}

export interface GetFolderViewsResponse {
  folderViews: IpcFolderView[];
}

export interface FolderViewCatalogChangedEvent {
  folderViews: IpcFolderView[];
}
