import { MbscTimezonePlugin } from './datetime.types.public';
interface IMomentTimezonePlugin extends MbscTimezonePlugin {
    moment?: any;
}
interface IDayjsTimezonePlugin extends MbscTimezonePlugin {
    dayjs?: any;
}
export declare const momentTimezone: IMomentTimezonePlugin;
export declare const dayjsTimezone: IDayjsTimezonePlugin;
export {};
