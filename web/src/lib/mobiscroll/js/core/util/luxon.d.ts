import { MbscTimezonePlugin } from './datetime.types.public';
interface ILuxonTimezonePlugin extends MbscTimezonePlugin {
    luxon?: any;
    version?: number;
}
export declare const luxonTimezone: ILuxonTimezonePlugin;
export {};
