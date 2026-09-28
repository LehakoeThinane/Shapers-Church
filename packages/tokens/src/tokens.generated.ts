// Generated from design/tokens/tokens.json by `pnpm tokens`. Do not edit by hand.

export const palettes = {
  "midnight": {
    "appearance": "dark",
    "color": {
      "background": "#050304",
      "glow": {
        "primary": "rgba(25,25,112,0.95)",
        "secondary": "rgba(8,10,48,0.95)",
        "accent": "rgba(212,178,111,0.22)",
        "tertiary": "rgba(25,25,112,0.65)",
        "edge": "rgba(70,90,210,0.45)"
      },
      "rim": [
        "rgba(232,200,130,0.95)",
        "rgba(70,90,210,0.55)",
        "rgba(25,25,112,0.10)",
        "rgba(70,90,210,0.50)",
        "rgba(232,200,130,0.70)"
      ],
      "glass": {
        "fill": "rgba(255,255,255,0.05)",
        "fillActive": "rgba(212,178,111,0.16)",
        "edge": "rgba(255,255,255,0.06)",
        "highlight": "rgba(255,236,200,0.28)",
        "sheen": "rgba(255,255,255,0.10)",
        "shadow": "rgba(0,0,0,0.55)"
      },
      "text": {
        "primary": "#F3EEE6",
        "secondary": "rgba(243,238,230,0.74)",
        "tertiary": "rgba(243,238,230,0.52)",
        "onAccent": "#0B0F2E",
        "onButton": "#0B0F2E"
      },
      "accent": "#D4B26F",
      "interactive": "#E0C07F",
      "button": {
        "primaryTop": "#E6C98C",
        "primaryBottom": "#C9A35C"
      },
      "track": "rgba(255,255,255,0.12)",
      "tile": "rgba(25,25,112,0.60)",
      "tileEdge": "rgba(212,178,111,0.45)",
      "danger": "#F2A08F",
      "success": "#9FD8B0",
      "video": {
        "start": "#03040C",
        "middle": "#191970",
        "end": "#0A0E2E",
        "glow": "rgba(212,178,111,0.45)"
      }
    }
  },
  "rose": {
    "appearance": "light",
    "color": {
      "background": "#EBE6E4",
      "glow": {
        "primary": "rgba(210,151,159,0.85)",
        "secondary": "rgba(181,165,158,0.90)",
        "accent": "rgba(210,151,159,0.55)",
        "tertiary": "rgba(181,165,158,0.70)",
        "edge": "rgba(0,0,0,0)"
      },
      "rim": [],
      "glass": {
        "fill": "rgba(255,255,255,0.42)",
        "fillActive": "rgba(255,255,255,0.70)",
        "edge": "rgba(255,255,255,0.75)",
        "highlight": "rgba(255,255,255,0.90)",
        "sheen": "rgba(255,255,255,0.45)",
        "shadow": "rgba(90,66,62,0.16)"
      },
      "text": {
        "primary": "#3A2E2B",
        "secondary": "rgba(58,46,43,0.78)",
        "tertiary": "rgba(58,46,43,0.58)",
        "onAccent": "#3A1B21",
        "onButton": "#EBE6E4"
      },
      "accent": "#D2979F",
      "interactive": "#9E5A64",
      "button": {
        "primaryTop": "#3F3230",
        "primaryBottom": "#2F2523"
      },
      "track": "rgba(58,46,43,0.14)",
      "tile": "rgba(210,151,159,0.40)",
      "tileEdge": "rgba(255,255,255,0.70)",
      "danger": "#A3372B",
      "success": "#2F6B45",
      "video": {
        "start": "#8E7C76",
        "middle": "#D2979F",
        "end": "#B5A59E",
        "glow": "rgba(235,230,228,0.70)"
      }
    }
  }
} as const;

export const font = {
  "ui": "-apple-system, \"SF Pro Text\", \"SF Pro Display\", Figtree, system-ui, sans-serif",
  "uiNative": "Figtree",
  "serif": "Newsreader, Georgia, serif",
  "serifNative": "Newsreader"
} as const;

export const radius = {
  "sm": 12,
  "md": 16,
  "lg": 20,
  "xl": 24,
  "card": 30,
  "pill": 999
} as const;

export const space = {
  "xxs": 4,
  "xs": 6,
  "sm": 10,
  "md": 14,
  "lg": 18,
  "xl": 22,
  "xxl": 32
} as const;

export const blur = {
  "glass": 26
} as const;
