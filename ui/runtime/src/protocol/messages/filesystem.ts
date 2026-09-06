export interface GetFilesystemEntriesRequest {
  path?: string;
  extensions?: string[];
  directoriesOnly?: boolean;
}

export interface FilesystemEntry {
  name: string;
  path: string;
  isDirectory: boolean;
}

export interface GetFilesystemEntriesResponse {
  path: string;
  parentPath?: string;
  entries: FilesystemEntry[];
  error?: {
    code: string;
    message: string;
  };
}
