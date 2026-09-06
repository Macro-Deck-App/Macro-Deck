import { downloadFile } from './download-file';

describe('downloadFile', () => {
  it('creates a transient anchor, clicks it and revokes the object URL', () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:test-url');
    spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    downloadFile(new Blob(['data']), 'pack.macroDeckIconPack');

    expect(URL.createObjectURL).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test-url');
    expect(document.querySelector('a[download]')).toBeNull();
  });

  it('sets the download file name', () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:test-url');
    spyOn(URL, 'revokeObjectURL');
    let anchor: HTMLAnchorElement | null = null;
    spyOn(HTMLAnchorElement.prototype, 'click').and.callFake(function (this: HTMLAnchorElement) {
      anchor = this;
    });

    downloadFile(new Blob(['data']), 'name.macroDeckIconPack');

    expect(anchor!.download).toBe('name.macroDeckIconPack');
  });
});
