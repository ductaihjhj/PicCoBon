---
version: alpha
name: Notion
description: |
  Notion's design system embodies a clean, minimal aesthetic built for
  productivity and clarity. The visual language prioritizes spaciousness,
  legible typography, and purposeful use of a single brand blue that anchors the
  entire interface. There is no visual noise—every element serves the user's
  focus on capturing knowledge and organizing work. The system embraces white
  space as a core tool, using it to separate information and reduce cognitive
  load. Headings employ generous negative letter-spacing that feels contemporary
  and confident. The color palette is intentionally restrained: one strong brand
  blue for interactive elements, a careful neutral scale for hierarchy and
  distinction, and thoughtful surface treatments that allow content to breathe.
source:
  url: "https://www.notion.com/"
  pagesAnalyzed: 1
  extractedAt: 2026-11-17
  tokensMeasured: true
colors:
  primary: "#0075DE"
  canvas: "#FFFFFF"
  surface-alt: "#F6F5F4"
  on-primary: "#FFFFFF"
  ink: "#000000"
  body: "#615D59"
  accent-1: "#097FE8"
  accent-2: "#005BAB"
  neutral-1: "#A39E98"
  neutral-2: "#E6F3FE"
typography:
  display-xxl:
    fontFamily: NotionInter
    fontSize: 96px
    fontWeight: 600
    lineHeight: 1.04
    letterSpacing: -4.6px
  display-xl:
    fontFamily: NotionInter
    fontSize: 72px
    fontWeight: 500
    lineHeight: 1.21
    letterSpacing: -2px
  display-lg:
    fontFamily: NotionInter
    fontSize: 54px
    fontWeight: 700
    lineHeight: 1.04
    letterSpacing: -1.88px
  display-md:
    fontFamily: NotionInter
    fontSize: 42px
    fontWeight: 700
    lineHeight: 1.14
    letterSpacing: -1.5px
  heading-sm:
    fontFamily: NotionInter
    fontSize: 22px
    fontWeight: 700
    lineHeight: 1.27
    letterSpacing: -0.25px
  heading-xs:
    fontFamily: NotionInter
    fontSize: 16px
    fontWeight: 700
    lineHeight: 1.5
    letterSpacing: 0px
  quote:
    fontFamily: "Lyon Text"
    fontSize: 18px
    fontWeight: 400
    lineHeight: 1.56
    letterSpacing: 0px
  body-xl:
    fontFamily: NotionInter
    fontSize: 20px
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: -0.12px
  body-lg:
    fontFamily: NotionInter
    fontSize: 16px
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: 0px
  body-lg-lyon-text:
    fontFamily: "Lyon Text"
    fontSize: 16px
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: 0px
  body-lg-strong:
    fontFamily: NotionInter
    fontSize: 16px
    fontWeight: 500
    lineHeight: 1.5
    letterSpacing: 0px
  body-md:
    fontFamily: NotionInter
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1.43
    letterSpacing: 0px
  body-md-strong:
    fontFamily: NotionInter
    fontSize: 14px
    fontWeight: 500
    lineHeight: 1.43
    letterSpacing: 0px
  caption:
    fontFamily: NotionInter
    fontSize: 12px
    fontWeight: 500
    lineHeight: 1.33
    letterSpacing: 0.13px
rounded:
  none: 0px
  xs: 4px
  sm: 8px
  md: 12px
  full: 9999px
spacing:
  xxs: 4px
  xs: 8px
  sm: 12px
  md: 16px
  lg: 20px
  xl: 24px
  xxl: 32px
  xxxl: 36px
  section: 40px
  band: 64px
borderWidths:
  thin: 1px
shadows:
  sm: "rgba(0, 0, 0, 0.01) 0px 0.175px 1.041px 0px, rgba(0, 0, 0, 0.02) 0px 0.8px 2.925px 0px, rgba(0, 0, 0, 0.027) 0px 2.025px 7.847px 0px, rgba(0, 0, 0, 0.04) 0px 4px 18px 0px"
elevationStrategy: layered-micro
themes:
  derived: dark   # the other theme is the site's measured palette
  light:
    bg: "#FFFFFF"
    surface: "#F6F5F4"
    surfaceRaised: "#ECEBEA"
    text: "#000000"
    textMuted: "#615D59"
    border: "#E0E0E0"
    accent: "#0075DE"
    accentFg: "#000000"
    focusRing: "#0075DE"
    elevation: shadow
  dark:
    bg: "#090F16"
    surface: "#181D24"
    surfaceRaised: "#242930"
    text: "#F5F9FE"
    textMuted: "#9BA0A6"
    border: "#30353B"
    accent: "#0080F2"
    accentFg: "#0B0B0C"
    focusRing: "#0075DE"
    elevation: "border+surface"
gradients:
  - context: section
    kind: linear
    value: "linear-gradient(rgba(0, 0, 0, 0) 35%, rgba(0, 0, 0, 0.55) 100%)"
components:
  button-primary:
    typography: "{typography.body-lg-strong}"
    textColor: "{colors.on-primary}"
    height: 38px
    padding: "6px 15px 6px 15px"
    rounded: "{rounded.sm}"
    backgroundColor: "{colors.primary}"
  button-filled:
    typography: "{typography.body-lg-strong}"
    textColor: "{colors.accent-2}"
    height: 38px
    padding: "6px 15px 6px 15px"
    rounded: "{rounded.sm}"
    backgroundColor: "{colors.neutral-2}"
  button-secondary:
    typography: "{typography.body-lg}"
    textColor: "{colors.on-primary}"
    height: 32px
    rounded: "50%"
    backgroundColor: "rgba(0, 0, 0, 0.05)"
  button-outline:
    typography: "{typography.body-md-strong}"
    textColor: "rgba(0, 0, 0, 0.95)"
    border: "1px solid rgba(0, 0, 0, 0.1)"
    height: 38px
    padding: "8px 16px 8px 16px"
    rounded: 9999.01px
    backgroundColor: "{colors.canvas}"
  button-text:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.898)"
    height: 30px
    padding: "5px 10px 5px 10px"
    rounded: "{rounded.xs}"
  button-text-sm:
    typography: "{typography.body-md}"
    textColor: "rgba(0, 0, 0, 0.54)"
    height: 20px
  card:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.898)"
    rounded: "{rounded.md}"
    backgroundColor: "{colors.canvas}"
  card-sm:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.898)"
  navigation:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.898)"
    height: 64px
    backgroundColor: "{colors.canvas}"
  footer:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.898)"
    padding: "80px 0px 24px 0px"
    backgroundColor: "{colors.canvas}"
  link:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.95)"
    rounded: "{rounded.md}"
  link-sm:
    typography: "{typography.body-lg}"
    textColor: "rgba(0, 0, 0, 0.95)"
states:
  other-hover:
    target: other
    state: hover
    opacity: 0.75
  other-focus:
    target: other
    state: focus
    boxShadow: "rgba(35, 131, 226, 0.57) 0px 0px 0px 1px inset, rgba(35, 131, 226, 0.35) 0px 0px 0px 2px"
  other-focus-visible:
    target: other
    state: focus-visible
    textDecoration: none
  link-hover:
    target: link
    state: hover
    textDecoration: underline
  link-focus:
    target: link
    state: focus
    textDecoration: underline
  link-active:
    target: link
    state: active
    textDecoration: underline
  other-disabled:
    target: other
    state: disabled
    opacity: 0.5
  other-active:
    target: other
    state: active
    opacity: 1
  button-hover:
    target: button
    state: hover
    textDecoration: none
breakpoints:
  - width: 375
    containerWidth: 343
    gridColumns: 3
    navLinksVisible: 25
    menuToggleVisible: true
    headingPx: 42
    bodyPx: 16
    sectionPaddingX: 16
  - width: 768
    containerWidth: 653
    gridColumns: 3
    navLinksVisible: 25
    menuToggleVisible: true
    headingPx: 61
    bodyPx: 16
    sectionPaddingX: 58
  - width: 1024
    containerWidth: 870
    gridColumns: 3
    navLinksVisible: 26
    menuToggleVisible: true
    headingPx: 90
    bodyPx: 16
    sectionPaddingX: 77
  - width: 1280
    containerWidth: 1094
    gridColumns: 3
    navLinksVisible: 28
    menuToggleVisible: true
    headingPx: 96
    bodyPx: 16
    sectionPaddingX: 93
  - width: 1440
    containerWidth: 1229
    gridColumns: 3
    navLinksVisible: 28
    menuToggleVisible: true
    headingPx: 96
    bodyPx: 16
    sectionPaddingX: 106
coverage:
  statesFound: 62
  gradientsFound: 1
  rolesUnassigned: 4
  archetypesUnnamed: 0
  archetypesDetected: 0
  responsiveMeasured: true
  stylesheetsBlocked: true
  semanticRampDeclared: false
---

# Design System Inspired by Notion

## 1. Visual Theme & Atmosphere

Notion's design system embodies a clean, minimal aesthetic built for productivity and clarity. The visual language prioritizes spaciousness, legible typography, and purposeful use of a single brand blue that anchors the entire interface. There is no visual noise—every element serves the user's focus on capturing knowledge and organizing work. The system embraces white space as a core tool, using it to separate information and reduce cognitive load. Headings employ generous negative letter-spacing that feels contemporary and confident. The color palette is intentionally restrained: one strong brand blue for interactive elements, a careful neutral scale for hierarchy and distinction, and thoughtful surface treatments that allow content to breathe.

**Key Characteristics**

- Minimal, clean aesthetic with generous whitespace
- Single brand blue as the primary interactive accent
- Negative letter-spacing on display typography for visual weight
- Flat, shadow-based elevation system with layered micro-shadows
- Sharp corners on cards (0px radius) contrasting with rounded buttons (8px radius)
- Color-blocking strategy: depth comes from surface shifts, not shadow stacking
- Content-forward design that removes visual friction
- High contrast between text and backgrounds for accessibility

## 2. Color Palette & Roles

### Primary

- **Brand Accent** (`{colors.primary}` — `#0075DE`): Primary CTA buttons, brand mark, active states, interactive highlights. The defining color of the Notion brand.

### Accent Colors

- **Accent Decorative 1** (`{colors.accent-1}` — `#097FE8`): Decorative use; no specific functional role assigned.
- **Accent Decorative 2** (`{colors.accent-2}` — `#005BAB`): Decorative use; no specific functional role assigned.

### Neutral Scale

- **Canvas** (`{colors.canvas}` — `#FFFFFF`): Default page background and primary surface. Also used as text color on brand surfaces.
- **Surface Alt** (`{colors.surface-alt}` — `#F6F5F4`): Alternating section bands and secondary surfaces for visual separation.
- **Ink** (`{colors.ink}` — `#000000`): Headings and primary text; highest contrast for maximum readability.
- **Body** (`{colors.body}` — `#615D59`): Body copy and secondary text; warm, slightly desaturated to reduce harshness.
- **Neutral 1** (`{colors.neutral-1}` — `#A39E98`): Tertiary text and muted UI elements.
- **Neutral 2** (`{colors.neutral-2}` — `#E6F3FE`): Light blue neutral used for background tints and subtle containers.

## 3. Typography Rules

### Font Family

**Primary:** NotionInter (sans-serif)
- Fallback: `-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Helvetica Neue', sans-serif`

**Secondary:** Lyon Text (serif, used sparingly for quotes and editorial content)
- Fallback: `'Georgia', serif`

### Hierarchy

| Role | Font | Size | Weight | Line Height | Letter Spacing | Notes |
|---|---|---|---|---|---|---|
| Display XXL | NotionInter | 96px | 600 | 1.04 | −4.6px | Hero headlines; maximum visual impact |
| Display XL | NotionInter | 72px | 500 | 1.21 | −2px | Large section headers |
| Display MD | NotionInter | 42px | 700 | 1.14 | −1.5px | Subheadings |
| Display LG | NotionInter | 54px | 700 | 1.04 | −1.88px | Feature headlines |
| Heading SM | NotionInter | 22px | 700 | 1.27 | −0.25px | Card and section titles |
| Heading XS | NotionInter | 16px | 700 | 1.5 | 0px | Small headings and labels |
| Body XL | NotionInter | 20px | 400 | 1.4 | −0.12px | Large body text and introductions |
| Body LG | NotionInter | 16px | 400 | 1.5 | 0px | Standard link and emphasis text |
| Body LG Strong | NotionInter | 16px | 500 | 1.5 | 0px | Medium-weight body text |
| Body LG Lyon Text | Lyon Text | 16px | 400 | 1.5 | 0px | Quoted or editorial text |
| Body MD | NotionInter | 14px | 400 | 1.43 | 0px | Standard body copy |
| Body MD Strong | NotionInter | 14px | 500 | 1.43 | 0px | Medium-weight body |
| Quote | Lyon Text | 18px | 400 | 1.56 | 0px | Pull quotes and testimonials |
| Caption | NotionInter | 12px | 500 | 1.33 | 0.13px | Small captions and metadata |

### Principles

- Negative letter-spacing on display sizes (`{typography.display-xxl}` through `{typography.display-md}`) creates visual tension and confidence.
- Line heights scale with size: larger text has tighter line heights (1.04–1.21) for compact headlines; body text uses 1.4–1.5 for comfort.
- Weight increases on headings (600–700) to establish clear hierarchy without relying on color shifts.
- Lyon Text serif is reserved for quotes and editorial moments, breaking the geometric NotionInter throughout.
- Body copy uses a warm neutral (`{colors.body}` — `#615D59`) to reduce eye strain while maintaining high contrast.

## 4. Component Stylings

### Buttons

**Primary**
- Background: `{colors.primary}` (`#0075DE`)
- Text Color: `{colors.canvas}` (`#FFFFFF`)
- Font: `{typography.body-lg-strong}` (16px, weight 500, line-height 24px)
- Padding: `6px 15px`
- Height: 38px
- Border Radius: `{rounded.sm}` (8px)
- Border: `1px solid transparent`
- Box Shadow: none
- Width: 155px
- Hover: Reduce opacity to 0.75; maintain text color `#FFFFFF`
- Focus: Focus ring outline (blue inset shadow: `rgba(35, 131, 226, 0.57) 0px 0px 0px 1px inset, rgba(35, 131, 226, 0.35) 0px 0px 0px 2px`)

**Secondary (Icon Button)**
- Background: `rgba(0, 0, 0, 0.05)`
- Text Color: `{colors.canvas}` (`#FFFFFF`)
- Font: `{typography.body-lg}` (16px, weight 400, line-height 24px)
- Padding: 0px
- Height: 32px
- Width: 32px
- Border Radius: `{rounded.full}` (9999px / circular)
- Border: none
- Box Shadow: none
- Hover: Opacity 0.7

**Filled / Secondary Variant**
- Background: `{colors.neutral-2}` (`#E6F3FE`)
- Text Color: `{colors.accent-2}` (`#005BAB`)
- Font: `{typography.body-lg-strong}` (16px, weight 500, line-height 24px)
- Padding: `6px 15px`
- Height: 38px
- Border Radius: `{rounded.sm}` (8px)
- Border: `1px solid transparent`
- Box Shadow: none
- Width: 155px

**Outline / Tertiary**
- Background: `{colors.canvas}` (`#FFFFFF`)
- Text Color: `rgba(0, 0, 0, 0.95)`
- Font: `{typography.body-md-strong}` (14px, weight 500, line-height 20px)
- Padding: `8px 16px`
- Height: 38px
- Border Radius: `{rounded.full}` (9999px)
- Border: `1px solid rgba(0, 0, 0, 0.1)`
- Box Shadow: none
- Width: 164px
- Hover: No fill change; text remains dark

**Ghost / Text Button**
- Background: transparent
- Text Color: `rgba(0, 0, 0, 0.898)`
- Font: `{typography.body-lg}` (16px, weight 400, line-height 24px)
- Padding: `5px 10px`
- Height: 30px
- Border Radius: `{rounded.xs}` (4px)
- Border: none
- Box Shadow: none
- Width: 108px
- Hover: Background slight tint, opacity 1

**Text Small**
- Background: transparent
- Text Color: `rgba(0, 0, 0, 0.54)`
- Font: `{typography.body-md}` (14px, weight 400, line-height 20px)
- Padding: 0px
- Height: 20px
- Border Radius: `{rounded.none}` (0px)
- Border: none
- Box Shadow: none
- Width: 101px

### Cards & Containers

**Default Card**
- Background: `{colors.canvas}` (`#FFFFFF`)
- Text Color: `rgba(0, 0, 0, 0.898)`
- Font: `{typography.body-lg}` (16px, weight 400, line-height 24px)
- Padding: 0px (content handled separately)
- Border Radius: `{rounded.md}` (12px)
- Border: none
- Box Shadow: none
- Width: 602px
- Height: 560px
- Use: Feature cards, product showcases, elevated content blocks

**Default Card (Secondary)**
- Background: transparent
- Text Color: `rgba(0, 0, 0, 0.898)`
- Font: `{typography.body-lg}` (16px, weight 400, line-height 24px)
- Padding: 0px
- Border Radius: `{rounded.none}` (0px)
- Border: none
- Box Shadow: none
- Width: 1228px
- Height: 492px
- Use: Full-width content sections without visual container

### Navigation

**Default Navigation**
- Background: `{colors.canvas}` (`#FFFFFF`)
- Text Color: `rgba(0, 0, 0, 0.898)`
- Font: `{typography.body-lg}` (16px, weight 400, line-height 24px)
- Height: 64px
- Border: none
- Box Shadow: `rgba(0, 0, 0, 0) 0px 1px 0px 0px` (hairline separator, nearly invisible)
- Border Radius: `{rounded.none}` (0px)
- Padding: 0px (flex-distributed)
- Width: Full viewport
- Links: Use `{typography.body-lg}` with dark text. Hover state: color shift to link hover tone and underline. Focus state: outline (`2px solid {colors.ink}`).

### Links

**Default Link**
- Color: `rgba(0, 0, 0, 0.95)` (nearly black, minimal contrast shift)
- Font: `{typography.body-lg}` (16px, weight 400, line-height 24px)
- Text Decoration: none (default)
- Border: none
- Background: transparent
- Hover: Color becomes `{colors.primary}` (`#0075DE`), text-decoration becomes underline
- Focus: Color `{colors.primary}`, underline active
- Active (pressed): Color `{colors.accent-2}` (`#005BAB`), underline active

### Inputs & Forms

No form component data was extracted. See "Known Gaps" for form input styling limitations.

## 5. Layout Principles

### Spacing System

Notion uses an 8px base unit scaled across 10 defined intervals:

- **`{spacing.xxs}`** = 4px: Minimal internal padding in compact components
- **`{spacing.xs}`** = 8px: Spacing between inline elements, button padding
- **`{spacing.sm}`** = 12px: Small gaps, input spacing
- **`{spacing.md}`** = 16px: Standard padding within containers, section margins
- **`{spacing.lg}`** = 20px: Breathing room between sections
- **`{spacing.xl}`** = 24px: Generous spacing for visual separation
- **`{spacing.xxl}`** = 32px: Large gaps between major blocks
- **`{spacing.xxxl}`** = 36px: Extra large separation
- **`{spacing.section}`** = 40px: Intra-section padding
- **`{spacing.band}`** = 64px: Between full-width section bands

**Usage Context:**
- Button internal padding: `{spacing.xs}` to `{spacing.sm}` (6–8px)
- Card and container padding: `{spacing.md}` to `{spacing.section}` (16–40px)
- Section margins: `{spacing.section}` to `{spacing.band}` (40–64px)
- Whitespace between text blocks: `{spacing.md}` to `{spacing.lg}` (16–20px)

### Grid & Container

The layout uses a flexible column-based approach:

- **Max Content Width:** 1094px–1229px depending on viewport (measured at 1024px and 1440px)
- **Column Strategy:** Measured as 3 columns across all viewport sizes, though actual column count may increase at larger sizes for flexible multi-column layouts
- **Section Padding (Horizontal):** Increases with viewport width for centered content
  - 375px: 16px padding per side
  - 768px: 58px padding per side
  - 1024px: 77px padding per side
  - 1280px: 93px padding per side
  - 1440px: 106px padding per side

### Whitespace Philosophy

Whitespace is the system's primary tool for clarity and hierarchy. Large margins and padding separate ideas; generous line heights make text comfortable to scan. The design resists cramping content, instead using full-width sections (`{spacing.band}` — 64px vertical separation) to establish breathing room. This reflects a productivity-first mentality: a user should never feel visually overwhelmed.

### Border Radius Scale

- **`{rounded.none}`** = 0px: Sharp corners on cards, overlays, and large containers (default)
- **`{rounded.xs}`** = 4px: Minimal rounding on text buttons and small UI elements
- **`{rounded.sm}`** = 8px: Primary button styling and moderate interactive elements
- **`{rounded.md}`** = 12px: Card containers and medium dropdowns
- **`{rounded.full}`** = 9999px: Circular / pill-shaped secondary buttons and icon containers

**Component Rounding:**
- Buttons: `{rounded.sm}` (8px) for primary; `{rounded.full}` (9999px) for secondary/icon buttons
- Cards: `{rounded.none}` (0px) for default; `{rounded.md}` (12px) for accent cards
- Overlays: `{rounded.none}` (0px)
- Inputs (if present): See "Known Gaps"

### Border Widths

- **Thin:** `1px` (used on button borders and hairline separators, e.g., navigation underline)

## 6. Depth & Elevation

| Level | Treatment | Use |
|---|---|---|
| Base (Flat) | No shadow | Buttons, text, large card blocks |
| Micro (Dropdown) | `rgba(0, 0, 0, 0.01) 0px 0.175px 1.041px 0px, rgba(0, 0, 0, 0.02) 0px 0.8px 2.925px 0px, rgba(0, 0, 0, 0.027) 0px 2.025px 7.847px 0px, rgba(0, 0, 0, 0.04) 0px 4px 18px 0px` | Dropdown menus, floating popovers, subtle lift |
| Hairline (Navigation) | `rgba(0, 0, 0, 0) 0px 1px 0px 0px` | Navigation bottom border (nearly invisible) |

**Shadow Philosophy:**

Notion uses a layered-micro elevation strategy. Rather than a single shadow, the system stacks multiple transparent layers (four separate shadows in the dropdown treatment) to create a barely-perceptible lift. This approach is contemporary and subtle—the shadow is felt rather than seen, adding depth without drawing attention. The opacity of each layer (0.01 to 0.04) means shadows are minimal; they exist to clarify layering, not to dominate. The navigation uses an almost-invisible separator (0px opacity black), indicating the system prefers color blocking over shadow for major transitions.

### Opacity Levels

- **80%** (`opacity: 0.80`): Hover state reduction for secondary interactions
- **42%** (`opacity: 0.42`): Medium opacity tint for muted or background layers
- **50%** (`opacity: 0.50`): Disabled element opacity
- **75%** (`opacity: 0.75`): Hover state for primary buttons (mild opacity reduction)
- **70%** (`opacity: 0.70`): Hover state for icon buttons

### Z-index / Layering

- **Base:** `z-index: 1–4` (stacked layers for internal component depth)
- **Sticky:** `z-index: 100` (navigation bars, persistent headers)

**Stacking Strategy:** The system uses a simplified z-index scale without modal/toast tiers. The sticky layer (100) ensures persistent UI stays above content; component internals use 1–4 for local stacking within cards or menus.

## 7. Do's and Don'ts

### Do

- **Maintain generous whitespace.** Use `{spacing.section}` and `{spacing.band}` to separate distinct ideas; never cram content.
- **Use the brand blue (`{colors.primary}` — `#0075DE`) sparingly for CTAs only.** It should draw the eye to the most important action on each page.
- **Default to the neutral scale for text.** Use `{colors.ink}` for headings, `{colors.body}` for body copy, and `{colors.neutral-1}` for tertiary text.
- **Apply negative letter-spacing to display typography.** This is a signature trait; preserve the exact tracking values.
- **Use flat, micro-shadow elevation.** Subtle stacked layers create depth without visual noise.
- **Keep button borders minimal.** Outline buttons use only `1px solid rgba(0, 0, 0, 0.1)` for restraint.
- **Respect component radius assignments.** Primary buttons are `{rounded.sm}` (8px), cards are `{rounded.none}` (sharp), secondary buttons are `{rounded.full}` (pills).
- **Build with color-blocking, not shadows, for major sections.** Alternate `{colors.canvas}` and `{colors.surface-alt}` to separate bands.

### Don't

- **Do not invent new colors.** The palette is intentionally small; any new state (error, warning, success) should be noted under "Known Gaps," not fabricated.
- **Do not round card corners to anything other than `{rounded.none}` or `{rounded.md}`.** Sharp corners are a deliberate choice on the default card.
- **Do not stack multiple shadows.** The dropdown micro-shadow is the only complex shadow; all other elements are flat or use the hairline border.
- **Do not reduce text size below `{typography.caption}` (12px) or body weight below 400 for readability.** The system has no tighter or lighter roles.
- **Do not apply hover effects to text links beyond color change and underline.** Do not add background fills or other decorations.
- **Do not use the accent decorative colors (`{colors.accent-1}`, `{colors.accent-2}`) for UI roles.** They have no assigned function and should appear only in illustrations or branding moments.
- **Do not override the focus ring.** Use the explicit blue inset shadow provided: `rgba(35, 131, 226, 0.57) 0px 0px 0px 1px inset, rgba(35, 131, 226, 0.35) 0px 0px 0px 2px`.
- **Do not use serif typography (Lyon Text) outside of quotes and editorial moments.** NotionInter is the system default.

## 8. Responsive Behavior

### Breakpoints

| Viewport | Content Width | Grid | Nav Links | Menu Toggle | Display XXL | Display LG | Body | Section PX |
|---|---|---|---|---|---|---|---|---|
| 375px (mobile) | 343px | 3 col | 25 visible | present | 42px | (N/A) | 16px | 16px |
| 768px (tablet) | 653px | 3 col | 25 visible | present | 61px | (N/A) | 16px | 58px |
| 1024px (desktop) | 870px | 3 col | 26 visible | present | 90px | 54px | 16px | 77px |
| 1280px (wide) | 1094px | 3 col | 28 visible | present | 96px | 54px | 16px | 93px |
| 1440px (ultra-wide) | 1229px | 3 col | 28 visible | present | 96px | 54px | 16px | 106px |

**Key Transitions:**
- Display typography scales continuously: 42px → 61px → 90px → 96px, reaching full size by 1280px.
- Section horizontal padding increases steadily (16px → 58px → 77px → 93px → 106px) to maintain margin-to-content ratio.
- Content column width grows to accommodate wider layouts while respecting max-width constraints.
- Navigation menu toggle remains visible across all breakpoints (no full collapse observed).
- Body text remains constant at 16px throughout, prioritizing readability.

### Touch Targets

Minimum touch target size: **32px–38px height** (observed in secondary buttons and primary button height). Small icon buttons measure 32×32px (`{rounded.full}`); standard interactive elements measure 38px tall. This ensures comfortable tap targets on mobile devices without breaking desktop layout.

### Collapsing Strategy

- **Navigation:** Menu toggle is consistently visible; desktop nav items stack horizontally and reduce in number at smaller widths (25→28 links), but no full hamburger collapse is evident from measurements.
- **Content Sections:** Padding shrinks but content remains in a single flow; no multi-column layout collapses to fewer columns.
- **Typography:** Display sizes scale down fluidly (96px down to 42px); no role is hidden or substituted.
- **Spacing:** `{spacing.section}` and `{spacing.band}` likely reduce proportionally; exact responsive spacing values not extracted.
- **Grid:** Remains 3-column across all sizes; no responsive column reflow measured.

## 9. Agent Prompt Guide

### Quick Color Reference

- **Primary CTA:** Brand Accent (`{colors.primary}` — `#0075DE`)
- **Page Background:** Canvas (`{colors.canvas}` — `#FFFFFF`)
- **Section Band (Alternating):** Surface Alt (`{colors.surface-alt}` — `#F6F5F4`)
- **Heading Text:** Ink (`{colors.ink}` — `#000000`)
- **Body Text:** Body (`{colors.body}` — `#615D59`)
- **Tertiary Text / Muted UI:** Neutral 1 (`{colors.neutral-1}` — `#A39E98`)
- **Light Blue Tint (backgrounds):** Neutral 2 (`{colors.neutral-2}` — `#E6F3FE`)

### Iteration Guide

1. **All text starts in the neutral scale.** Headings use `{colors.ink}` (#000000), body uses `{colors.body}` (#615D59), small text uses `{colors.neutral-1}` (#A39E98).
2. **Brand blue (`{colors.primary}` — #0075DE) is reserved for CTAs and active states only.** Do not use it for body text, secondary labels, or neutral elements.
3. **Button styling depends on prominence:**
   - Primary: Blue fill (#0075DE), white text, 8px radius, 38px height
   - Secondary: Light blue fill (#E6F3FE), dark text, 8px radius
   - Outline: White fill, dark text, dark border, 9999px radius (pill-shaped)
   - Ghost: Transparent, dark text, 4px radius
4. **Apply `{typography.display-xxl}` (96px, 600 weight, −4.6px tracking) to hero headlines; scale down for smaller viewports (42px at mobile).** Preserve the negative letter-spacing exactly.
5. **Cards default to sharp corners (`{rounded.none}` — 0px) and white backgrounds.** Only use `{rounded.md}` (12px) or alternate background colors when intentionally breaking the card grid.
6. **Separate sections with `{spacing.band}` (64px) or by alternating background colors between `{colors.canvas}` and `{colors.surface-alt}`.** Do not use shadows; color-block instead.
7. **Hover states on buttons reduce opacity (0.75–0.7); hover states on links change color to brand blue and add underline.** No other decorative effects.
8. **Dropdowns and floating elements use the four-layer micro-shadow.** All other elements are flat.
9. **Responsive: section padding increases with viewport width (16px → 106px); typography scales fluidly; no column count changes measured.**
10. **Always check the extracted role before assigning color.** If uncertain, default to the neutral scale and note it under "Known Gaps."

## 10. Known Gaps

- **Form Inputs & Controls:** No input field, textarea, checkbox, radio, or select styling was extracted. The design system covers buttons, navigation, and cards; form patterns are not documented here.
- **Semantic Status Colors:** No error, success, warning, or validation states are declared. The site provides no red, green, or yellow semantic palette. Any future status messaging will require new color definitions.
- **Interactive State Details:** Hover, focus, and active states exist in the stylesheet but are sparse. Complete state coverage (disabled, loading, pressed, focused) cannot be guaranteed; only observed states are reported.
- **Decorative Color Roles:** Four colors (`{colors.accent-1}` #097FE8, `{colors.accent-2}` #005BAB, and two neutral tones) lack assigned functional roles and appear only in illustrations or branding contexts. Do not assume they are meant for UI.
- **Dark Mode / Theme Variants:** The extraction covers one theme only (light mode). No dark theme or high-contrast variant was measured.
- **Animation & Transition Timing:** No duration, easing, or animation values were extracted. Behavior beyond static styling is not documented.
- **Cross-Origin Stylesheet Coverage:** Some stylesheets may be cross-origin and unreadable, so CSS-declared detail in third-party components may be missing.
- **Authentication-Protected Surfaces:** Pages behind login (e.g., workspace dashboards, settings) were not visited. UI patterns within authenticated areas are not included.
- **Gradient Decorations:** One linear gradient overlay was identified (`linear-gradient(rgba(0, 0, 0, 0) 35%, rgba(0, 0, 0, 0.55) 100%)`) but its context and use are limited. Full gradient scale is not defined.
- **Icon or Illustration Styling:** Icon sizing, stroke weights, and fill rules are not extracted. Icon libraries or SVG patterns are not documented.
- **Print Styles:** No print media queries or print-specific rules were extracted.