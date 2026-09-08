export interface StoredAssetReader {
  readonly key: string;
  readonly size: number;
  read(offset: number, length: number): Promise<Uint8Array>;
}

export interface AssetStore {
  put(key: string, data: Uint8Array): Promise<void>;
  open(key: string): Promise<StoredAssetReader | undefined>;
  /** Convenience for small assets. Prefer open()+read() for large packs. */
  get(key: string): Promise<Uint8Array | undefined>;
  has(key: string): Promise<boolean>;
  remove(key: string): Promise<boolean>;
  list(prefix?: string): Promise<readonly string[]>;
}

export function normalizeAssetKey(key: string): string {
  const normalized = key.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
  if (!normalized || normalized.split("/").some((segment) => !segment || segment === "." || segment === "..")) {
    throw new Error(`Invalid OBP asset key '${key}'`);
  }
  return normalized;
}

function assertRange(key: string, size: number, offset: number, length: number): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || offset < 0 || length < 0 || offset + length > size) {
    throw new RangeError(`Invalid stored-asset read ${key} @ ${offset}+${length} (size ${size})`);
  }
}

export class MemoryAssetStore implements AssetStore {
  private readonly files = new Map<string, Uint8Array>();

  async put(key: string, data: Uint8Array): Promise<void> {
    this.files.set(normalizeAssetKey(key), data.slice());
  }

  async open(key: string): Promise<StoredAssetReader | undefined> {
    const normalized = normalizeAssetKey(key);
    const data = this.files.get(normalized);
    if (!data) return undefined;
    return {
      key: normalized,
      size: data.length,
      async read(offset: number, length: number): Promise<Uint8Array> {
        assertRange(normalized, data.length, offset, length);
        return data.slice(offset, offset + length);
      },
    };
  }

  async get(key: string): Promise<Uint8Array | undefined> {
    const reader = await this.open(key);
    return reader?.read(0, reader.size);
  }

  async has(key: string): Promise<boolean> {
    return (await this.open(key)) !== undefined;
  }

  async remove(key: string): Promise<boolean> {
    return this.files.delete(normalizeAssetKey(key));
  }

  async list(prefix = ""): Promise<readonly string[]> {
    const cleanPrefix = prefix ? normalizeAssetKey(prefix) : "";
    return [...this.files.keys()].filter((key) => !cleanPrefix || key.startsWith(cleanPrefix)).sort();
  }
}

export class OpfsAssetStore implements AssetStore {
  constructor(private readonly rootName = "obp") {}

  async put(key: string, data: Uint8Array): Promise<void> {
    const { directory, filename } = await this.resolveParent(key, true);
    const handle = await directory.getFileHandle(filename, { create: true });
    const writable = await handle.createWritable();
    await writable.write(data);
    await writable.close();
  }

  async open(key: string): Promise<StoredAssetReader | undefined> {
    const normalized = normalizeAssetKey(key);
    try {
      const { directory, filename } = await this.resolveParent(normalized, false);
      const handle = await directory.getFileHandle(filename);
      const file = await handle.getFile();
      return {
        key: normalized,
        size: file.size,
        async read(offset: number, length: number): Promise<Uint8Array> {
          assertRange(normalized, file.size, offset, length);
          if (length === 0) return new Uint8Array();
          return new Uint8Array(await file.slice(offset, offset + length).arrayBuffer());
        },
      };
    } catch (error) {
      if (isNotFound(error)) return undefined;
      throw error;
    }
  }

  async get(key: string): Promise<Uint8Array | undefined> {
    const reader = await this.open(key);
    return reader?.read(0, reader.size);
  }

  async has(key: string): Promise<boolean> {
    return (await this.open(key)) !== undefined;
  }

  async remove(key: string): Promise<boolean> {
    try {
      const { directory, filename } = await this.resolveParent(key, false);
      await directory.removeEntry(filename);
      return true;
    } catch (error) {
      if (isNotFound(error)) return false;
      throw error;
    }
  }

  async list(prefix = ""): Promise<readonly string[]> {
    const root = await this.root();
    const output: string[] = [];
    await walkDirectory(root, "", output);
    const cleanPrefix = prefix ? normalizeAssetKey(prefix) : "";
    return output.filter((key) => !cleanPrefix || key.startsWith(cleanPrefix)).sort();
  }

  private async root(): Promise<FileSystemDirectoryHandle> {
    if (!navigator.storage?.getDirectory) throw new Error("OPFS is unavailable in this browser.");
    const originRoot = await navigator.storage.getDirectory();
    return originRoot.getDirectoryHandle(this.rootName, { create: true });
  }

  private async resolveParent(key: string, create: boolean): Promise<{ directory: FileSystemDirectoryHandle; filename: string }> {
    const segments = normalizeAssetKey(key).split("/");
    const filename = segments.pop();
    if (!filename) throw new Error("Asset key has no filename.");
    let directory = await this.root();
    for (const segment of segments) directory = await directory.getDirectoryHandle(segment, { create });
    return { directory, filename };
  }
}

async function walkDirectory(directory: FileSystemDirectoryHandle, prefix: string, output: string[]): Promise<void> {
  for await (const [name, handle] of directory.entries()) {
    const key = prefix ? `${prefix}/${name}` : name;
    if (handle.kind === "file") output.push(key);
    else await walkDirectory(handle as FileSystemDirectoryHandle, key, output);
  }
}

function isNotFound(error: unknown): boolean {
  return error instanceof DOMException && error.name === "NotFoundError";
}
