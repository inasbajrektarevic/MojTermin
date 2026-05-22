import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';
import intlTelInput from 'intl-tel-input';

export type PhoneCountryOption = {
  iso2: string;
  name: string;
  dialCode: string;
  flagUrl: string;
};

// Expected digit count for the LOCAL part (without country code, without leading 0).
// Values come from ITU-T E.164 / national numbering plans. Conservative ranges
// chosen to accept both mobile and landline where applicable. Countries not in
// this table fall back to `DEFAULT_PHONE_RULE` below.
export const PHONE_LENGTH_RULES: Record<string, { min: number; max: number; example: string }> = {
  ba: { min: 8, max: 8, example: '61 234 567' },
  hr: { min: 8, max: 9, example: '91 234 5678' },
  rs: { min: 8, max: 9, example: '64 123 4567' },
  me: { min: 8, max: 8, example: '67 123 456' },
  si: { min: 8, max: 8, example: '31 234 567' },
  at: { min: 9, max: 12, example: '660 123 4567' },
  de: { min: 10, max: 11, example: '151 1234 5678' },
  dk: { min: 8, max: 8, example: '12 34 56 78' },
  us: { min: 10, max: 10, example: '202 555 0100' }
};

// Permissive fallback for countries not explicitly listed above. Matches the
// server-side regex on RegionalPhone (6-15 digits in the full international form).
export const DEFAULT_PHONE_RULE = { min: 6, max: 14, example: '' };

// Priority order for the country dropdown — Balkans / EU regions first, then alphabetical.
const COUNTRY_PRIORITY_ORDER = ['ba', 'hr', 'rs', 'me', 'si', 'at', 'de', 'dk', 'us'];

function stripNonDigits(value: string | null | undefined): string {
  return (value ?? '').replace(/\D+/g, '');
}

/**
 * Strip non-digit characters and a single leading 0 (the domestic trunk prefix).
 * `061 234 567` and `61 234 567` both normalize to `61234567`.
 */
export function normalizeLocalPhone(value: string | null | undefined): string {
  const digits = stripNonDigits(value);
  return digits.startsWith('0') ? digits.slice(1) : digits;
}

/**
 * Combine the country dial code and the local part into an E.164-style string.
 * Returns empty when either part is missing.
 */
export function toInternationalPhone(
  iso2: string | null | undefined,
  localValue: string | null | undefined,
  countries: PhoneCountryOption[]
): string {
  const normalizedLocal = normalizeLocalPhone(localValue);
  if (!normalizedLocal) {
    return '';
  }
  const country = countries.find((x) => x.iso2 === (iso2 ?? '').toLowerCase());
  if (!country) {
    return '';
  }
  return `${country.dialCode}${normalizedLocal}`;
}

/**
 * Cross-field validator. Reads `phoneCountryIso2` + `phoneNumber` from the form
 * group, applies the per-country length rule from {@link PHONE_LENGTH_RULES},
 * and writes specific error keys onto the phone control so callers can render
 * targeted messages (`phoneTooShort`, `phoneTooLong`, `phoneInvalidChars`).
 *
 * @param phoneNumberKey  Name of the local-part control. Defaults to `phoneNumber`.
 * @param countryKey      Name of the iso2 country control. Defaults to `phoneCountryIso2`.
 */
export function phoneNumberValidator(
  phoneNumberKey: string = 'phoneNumber',
  countryKey: string = 'phoneCountryIso2'
): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const formGroup = group as FormGroup;
    const phoneControl = formGroup.get(phoneNumberKey);
    const countryControl = formGroup.get(countryKey);
    if (!phoneControl || !countryControl) {
      return null;
    }

    const raw = (phoneControl.value ?? '').toString();
    if (!raw.trim()) {
      // 'required' is enforced on the control itself; cross-field validator stays silent here.
      return null;
    }

    // Reject inputs with non-digit characters other than the common separators
    // we silently strip (space, dash, slash, parentheses, plus).
    if (/[^\d\s\-/()+]/.test(raw)) {
      phoneControl.setErrors({ ...(phoneControl.errors ?? {}), phoneInvalidChars: true });
      return null;
    }

    const iso2 = (countryControl.value ?? 'ba').toLowerCase();
    const rule = PHONE_LENGTH_RULES[iso2] ?? DEFAULT_PHONE_RULE;
    const digits = normalizeLocalPhone(raw);

    if (digits.length < rule.min) {
      phoneControl.setErrors({
        ...(phoneControl.errors ?? {}),
        phoneTooShort: { min: rule.min, example: rule.example }
      });
      return null;
    }

    if (digits.length > rule.max) {
      phoneControl.setErrors({
        ...(phoneControl.errors ?? {}),
        phoneTooLong: { max: rule.max, example: rule.example }
      });
      return null;
    }

    // Clear our phone-specific errors but preserve any others (e.g. required, maxlength).
    if (phoneControl.errors) {
      const { phoneTooShort, phoneTooLong, phoneInvalidChars, ...rest } = phoneControl.errors;
      const remaining = Object.keys(rest).length ? rest : null;
      phoneControl.setErrors(remaining);
    }
    return null;
  };
}

/**
 * Render an end-user friendly error message for a phone field that uses the
 * keys produced by {@link phoneNumberValidator}. Returns null when there is
 * nothing to show (control is untouched or has no phone-specific error).
 */
export function describePhoneError(
  errors: ValidationErrors | null,
  country: PhoneCountryOption | undefined
): string | null {
  if (!errors) {
    return null;
  }

  const countryLabel = country
    ? `${country.iso2.toUpperCase()} (${country.dialCode})`
    : 'odabranu državu';

  if (errors['phoneInvalidChars']) {
    return 'Broj telefona smije sadržavati samo cifre i razmake.';
  }

  if (errors['phoneTooShort']) {
    const { min, example } = errors['phoneTooShort'] as { min: number; example: string };
    const suffix = example ? `, npr. ${example}` : '';
    return `Broj je prekratak za ${countryLabel} — unesite najmanje ${min} cifara${suffix}.`;
  }

  if (errors['phoneTooLong']) {
    const { max, example } = errors['phoneTooLong'] as { max: number; example: string };
    const suffix = example ? `, npr. ${example}` : '';
    return `Broj je predug za ${countryLabel} — najviše ${max} cifara${suffix}.`;
  }

  return null;
}

/**
 * Build the list of countries shown in the country picker. Pulls the full
 * country data from intl-tel-input and re-orders it so the priority countries
 * (Balkans, key EU markets) appear at the top.
 */
export function buildPhoneCountries(): PhoneCountryOption[] {
  const priorityIndex = new Map(COUNTRY_PRIORITY_ORDER.map((iso2, index) => [iso2, index]));
  // intl-tel-input ships its own (untyped) country list. Treating the items as
  // any here is intentional — the library does not expose strict types.
  const countries = intlTelInput.getCountryData() as Array<{ iso2: string; name: string; dialCode: string }>;

  return countries
    .map((country) => ({
      iso2: country.iso2,
      name: country.name,
      dialCode: `+${country.dialCode}`,
      flagUrl: `https://flagcdn.com/24x18/${country.iso2.toLowerCase()}.png`
    }))
    .sort((a, b) => {
      const aPriority = priorityIndex.get(a.iso2);
      const bPriority = priorityIndex.get(b.iso2);

      if (aPriority !== undefined && bPriority !== undefined) {
        return aPriority - bPriority;
      }
      if (aPriority !== undefined) {
        return -1;
      }
      if (bPriority !== undefined) {
        return 1;
      }

      return a.name.localeCompare(b.name);
    });
}
