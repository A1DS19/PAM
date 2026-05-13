import { themes as prismThemes } from "prism-react-renderer";
import type { Config } from "@docusaurus/types";
import type * as Preset from "@docusaurus/preset-classic";

const config: Config = {
  title: "PAM Docs",
  tagline: "Player Admin Manager — architecture & reference",
  favicon: "img/favicon.ico",

  future: {
    v4: true,
  },

  url: "https://pam-docs.local",
  baseUrl: "/",

  organizationName: "pam",
  projectName: "pam-docs",

  // Relative links inside the existing markdown can resolve weirdly until
  // every doc gets a small migration pass; warn loudly, don't fail builds.
  onBrokenLinks: "warn",
  onBrokenAnchors: "warn",
  onBrokenMarkdownLinks: "warn",

  // Mermaid renders inside Docusaurus when the markdown plugin sees a
  // ```mermaid fenced block. Combined with @docusaurus/theme-mermaid below.
  markdown: {
    mermaid: true,
  },

  themes: ["@docusaurus/theme-mermaid"],

  i18n: {
    defaultLocale: "en",
    locales: ["en"],
  },

  presets: [
    [
      "classic",
      {
        docs: {
          // Markdown lives in the conventional Docusaurus location
          // (docs-site/docs/). Files under internal/ exist on disk for
          // reference and code-side links but are excluded from the site.
          routeBasePath: "/",
          sidebarPath: "./sidebars.ts",
          exclude: ["internal/**", "**/node_modules/**"],
        },
        blog: false,
        theme: {
          customCss: "./src/css/custom.css",
        },
      } satisfies Preset.Options,
    ],
  ],

  plugins: [
    [
      // Offline, in-browser search. No Algolia account, no API keys.
      // hashed: 'full' lets us deep-link search results in static deploys.
      require.resolve("@easyops-cn/docusaurus-search-local"),
      {
        hashed: true,
        docsRouteBasePath: "/",
        indexBlog: false,
        highlightSearchTermsOnTargetPage: true,
      },
    ],
  ],

  themeConfig: {
    image: "img/pam-social-card.jpg",
    colorMode: {
      defaultMode: "dark",
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: "PAM",
      logo: {
        alt: "PAM",
        src: "img/logo.svg",
      },
      items: [
        {
          type: "docSidebar",
          sidebarId: "pamSidebar",
          position: "left",
          label: "Docs",
        },
        {
          href: "https://github.com/A1DS19/PAM",
          label: "GitHub",
          position: "right",
        },
      ],
    },
    footer: {
      style: "dark",
      links: [
        {
          title: "Architecture",
          items: [
            { label: "Architecture", to: "/ARCHITECTURE" },
            { label: "Core platform mapping", to: "/CORE_PLATFORM_MAPPING" },
          ],
        },
        {
          title: "Modules",
          items: [
            { label: "Authentication", to: "/AUTH" },
            { label: "Ingest", to: "/INGEST" },
          ],
        },
        {
          title: "Operations",
          items: [
            { label: "Endpoints", to: "/ENDPOINTS" },
            { label: "Local development", to: "/LOCAL_DEV" },
            { label: "Testing", to: "/TESTING" },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} PAM. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ["csharp", "bash", "json", "sql", "yaml"],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
