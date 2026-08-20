import { MbscRecurrenceRule } from './recurrence.types.public';
export declare type MbscDateType = string | Date | object;
export interface MbscCalendarInvalid {
    /** If true the specified invalid will cover the whole day. */
    allDay?: boolean;
    /** Specifies the start of the invalid range. */
    start?: MbscDateType;
    /** Specifies the end of the invalid range. */
    end?: MbscDateType;
    /** Recurrence rule for recurring invalid ranges. */
    recurring?: MbscRecurrenceRule | string;
    /**
     * Specifies recurring exceptions.
     * Useful when specific dates need to be skipped from the rule.
     */
    recurringException?: MbscDateType[] | MbscDateType;
    /**
     * Specifies a recurrence exception rule.
     * Useful when recurring dates need to be skipped from the rule.
     */
    recurringExceptionRule?: MbscRecurrenceRule | string;
}
export interface MbscTimezonedDate extends Date {
    /**
     * @hidden
     * It's a flag indicating that the Date Object as an Mobiscroll Date
     */
    _mbsc?: boolean;
    /**
     * Switches the timezone of the Date
     * @param timezone The timezone to switch the date to
     */
    setTimezone(timezone: string): void;
    /**
     * Returns a new Date object initialized with the same values.
     */
    clone(): MbscTimezonedDate;
    createDate(value: number | Date): MbscTimezonedDate;
    createDate(year?: number | string | MbscTimezonedDate | Date, month?: number, date?: number, h?: number, min?: number, sec?: number, ms?: number): MbscTimezonedDate;
}
export interface MbscTimezonePlugin {
    /** @hidden */
    isMoment?: boolean;
    parse(date: number | string, s: any): MbscTimezonedDate;
    createDate(s: any, year?: number | string | MbscTimezonedDate | Date, month?: number, date?: number, h?: number, min?: number, sec?: number, ms?: number): MbscTimezonedDate;
    /** @hidden */
    getLib(): any;
}
