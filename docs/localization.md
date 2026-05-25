# Localization (Qatar / ar-QA + en-QA)

This app uses **ASP.NET Core request localization** with **`.resx`** resources under `SmartTrafficTool/Resources/`.

## Cultures

| Culture | Role |
| -------- | ---- |
| `ar-QA` | Default UI (Arabic, Qatar) |
| `en-QA` | Secondary (English, Qatar style) |

Culture is stored in the **`.AspNetCore.Culture`** cookie (via `CookieRequestCultureProvider`) and can be switched from the header on every page.

## Where strings live

- **Default (English) strings:** `SmartTrafficTool/Resources/SharedResource.resx`
- **Arabic overrides:** `SmartTrafficTool/Resources/SharedResource.ar-QA.resx`
- **Accessor in Razor / controllers:** `@inject IStringLocalizer<SharedResource> L` (see `Views/_ViewImports.cshtml`)

### Naming convention

- **`Nav_*`**, **`Title_*`**, **`PageSubtitle_*`**: navigation & page chrome
- **`Search_*`**, **`Forensic_*`**, **`Poc_*`**: feature areas
- **`Js_*`**, keys in `window.smartTrafficI18n` (see `_Layout.cshtml`): **JavaScript-visible** copy (tooltips, chart labels, map widgets, voice/TTS)
- **`Copilot_*`**: AI copilot replies/widgets (server-generated JSON for the panel)

When adding a key, add it to **both** `SharedResource.resx` and `SharedResource.ar-QA.resx` unless you intentionally want a fallback to neutral English.

## Bootstrap + RTL

- `_Layout.cshtml` switches between `bootstrap.css` and **`bootstrap.rtl.css`** when the UI culture is RTL.
- The `<body>` gets a `rtl` class (`app-body rtl`) plus `lang`/`dir` on `<html>` for accessibility.

## Adding a string (checklist)

1. Add `<data name="Your_Key" xml:space="preserve"><value>...</value></data>` to **both** resx files.
   Avoid raw `&nbsp;` in `.resx` values (prefer normal spaces).
2. Use `L["Your_Key"]` in Razor, or `string.Format(CultureInfo.CurrentUICulture, L["Your_Key"].Value, args…)` when formatting numbers/dates already rendered as strings.
3. For **script-visible** text, also wire the key inside the `smartTrafficI18n` dictionary in `_Layout.cshtml`.

## RTL / bilingual smoke checklist

Manual checks after substantive UI changes:

- [ ] **Cookie**: switch **EN / العربية**, reload pages — chrome, nav labels, subtitles update.
- [ ] **Direction**: Arabic mode sets **`dir="rtl"`** without overlapping sidebar/topbar content.
- [ ] **Search**: form, tabs (map/card/list), result cards & list actions, sidebar summary + charts tooltips use the active language.
- [ ] **Map**: marker popup strings, stacked navigation, investigation **live-track** chip.
- [ ] **AI Anomalies**: all three forensic tabs; behavioural chart labels follow culture.
- [ ] **POC simulator**: validation messages, toolbar, path-builder button titles (including JS-rendered rows).
- [ ] **Co-pilot**: help card, navigate/search replies, insights widgets localize.
- [ ] **LTR islands**: plate text, numeric IDs (`VEH-…`, device numbers) stay readable without broken shaping.
