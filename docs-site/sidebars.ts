import type { SidebarsConfig } from "@docusaurus/plugin-content-docs";

// Explicit ordering — no frontmatter required on the source markdown.
// Each "id" is the file path relative to docs-site/docs/ without `.md`.
// Files under internal/ are excluded by docusaurus.config.ts.
const sidebars: SidebarsConfig = {
  pamSidebar: [
    {
      type: "category",
      label: "Architecture",
      collapsible: false,
      items: ["ARCHITECTURE", "CORE_PLATFORM_MAPPING"],
    },
    {
      type: "category",
      label: "Modules",
      collapsed: false,
      items: ["AUTH", "INGEST"],
    },
    {
      type: "category",
      label: "Operations",
      collapsed: false,
      items: ["LOCAL_DEV", "TESTING"],
    },
    {
      type: "category",
      label: "Reference",
      collapsed: false,
      items: ["ENDPOINTS"],
    },
  ],
};

export default sidebars;
