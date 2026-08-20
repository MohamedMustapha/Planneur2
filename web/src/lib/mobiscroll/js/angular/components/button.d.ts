import { ButtonBase } from '../../core/components/button/button';
import { MbscCustomEndIcon, MbscCustomIcon, MbscCustomStartIcon } from './icon';
import * as i0 from "@angular/core";
export declare class MbscButton extends ButtonBase {
    /** @hidden */
    _customIcon?: MbscCustomIcon;
    /** @hidden */
    _customStartIcon?: MbscCustomStartIcon;
    /** @hidden */
    _customEndIcon?: MbscCustomEndIcon;
    /** {@inheritDoc MbscButtonOptions.ariaLabel} */
    ariaLabel?: string;
    /** {@inheritDoc MbscButtonOptions.color} */
    color?: 'primary' | 'secondary' | 'success' | 'danger' | 'warning' | 'info' | 'dark' | 'light';
    /** {@inheritDoc MbscButtonOptions.disabled} */
    disabled?: boolean;
    /** {@inheritDoc MbscButtonOptions.endIcon} */
    endIcon?: string;
    /** @hidden */
    endIconSrc?: string;
    /** {@inheritDoc MbscButtonOptions.endIconSvg} */
    endIconSvg?: string;
    /** @hidden */
    hidden?: boolean;
    /** {@inheritDoc MbscButtonOptions.icon} */
    icon?: string;
    /** {@inheritDoc MbscButtonOptions.iconSvg} */
    iconSvg?: string;
    /** @hidden */
    iconSrc?: string;
    /** @hidden */
    ripple?: boolean;
    /** {@inheritDoc MbscButtonOptions.role} */
    role?: 'button' | 'none';
    /** {@inheritDoc MbscButtonOptions.startIcon} */
    startIcon?: string;
    /** @hidden */
    startIconSrc?: string;
    /** {@inheritDoc MbscButtonOptions.startIconSvg} */
    startIconSvg?: string;
    /** {@inheritDoc MbscButtonOptions.tabIndex} */
    tabIndex?: number;
    /** {@inheritDoc MbscButtonOptions.variant} */
    variant?: 'standard' | 'flat' | 'outline';
    static ɵfac: i0.ɵɵFactoryDeclaration<MbscButton, never>;
    static ɵcmp: i0.ɵɵComponentDeclaration<MbscButton, "mbsc-button", never, { "ariaLabel": "ariaLabel"; "color": "color"; "disabled": "disabled"; "endIcon": "endIcon"; "endIconSrc": "endIconSrc"; "endIconSvg": "endIconSvg"; "hidden": "hidden"; "icon": "icon"; "iconSvg": "iconSvg"; "iconSrc": "iconSrc"; "ripple": "ripple"; "role": "role"; "startIcon": "startIcon"; "startIconSrc": "startIconSrc"; "startIconSvg": "startIconSvg"; "tabIndex": "tabIndex"; "variant": "variant"; }, {}, ["_customIcon", "_customStartIcon", "_customEndIcon"], ["[mbsc-icon]", "[mbsc-start-icon]", "*", "[mbsc-end-icon]"]>;
}
