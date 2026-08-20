declare type Constructor = new (...args: any[]) => any;
export declare function FormControl<T extends Constructor>(constructor: T): void;
export {};
