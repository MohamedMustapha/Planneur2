/**
 * The one Node built-in the token test uses.
 *
 * Declared here rather than pulling in `@types/node`: only a test reads the filesystem, and the whole surface it
 * needs is one function. A types package would put every Node global in scope of the browser bundle's compile.
 */
declare module 'node:fs' {
  export function readFileSync(path: string, encoding: 'utf8'): string;
}
