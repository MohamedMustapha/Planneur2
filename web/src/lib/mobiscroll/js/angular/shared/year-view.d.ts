import { OnChanges } from '@angular/core';
import * as i0 from "@angular/core";
export declare class MbscYearView implements OnChanges {
    active: number;
    activeMonth: number;
    getDate: (y: number, m: number, d: number) => Date;
    getMonth: (d: Date) => number;
    getYear: (d: Date) => number;
    max: number | Date;
    min: number | Date;
    monthNames: string[];
    monthNamesShort: string[];
    rtl?: boolean;
    theme?: string;
    year: number;
    onMonthClick?: (args: any) => void;
    _rows: any;
    _selectedYear?: number;
    _selectedMonth?: number;
    _activeYear?: number;
    _activeMonth?: number;
    ngOnChanges(): void;
    getKey(index: number, item: any): any;
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscYearView, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscYearView, "mbsc-year-view", never, { "active": "active"; "activeMonth": "activeMonth"; "getDate": "getDate"; "getMonth": "getMonth"; "getYear": "getYear"; "max": "max"; "min": "min"; "monthNames": "monthNames"; "monthNamesShort": "monthNamesShort"; "rtl": "rtl"; "theme": "theme"; "year": "year"; "onMonthClick": "onMonthClick"; }, {}, never, never>;
}
