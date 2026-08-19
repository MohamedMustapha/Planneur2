// Typings shim for the ESM bundle.
//
// Mobiscroll ships two builds and only puts .d.ts files next to the UMD one. We import the ESM build (the UMD
// wrapper calls require('@angular/core') at runtime, which ng serve cannot satisfy), so the types have to be
// pointed at from here. Hand-written: re-extracting the zip over web/src/lib will delete this file — put it back.
export * from '../js/public_api';
