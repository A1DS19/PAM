import type { ReactNode } from "react";
import clsx from "clsx";
import Link from "@docusaurus/Link";
import useDocusaurusContext from "@docusaurus/useDocusaurusContext";
import Layout from "@theme/Layout";
import Heading from "@theme/Heading";
import Mermaid from "@theme/Mermaid";

import styles from "./index.module.css";

const platformDiagram = `
flowchart TB
  classDef api fill:#3b6cf2,color:#fff,stroke:#1f55ed,stroke-width:1px
  classDef module fill:#1a233b,color:#e8eeff,stroke:#3b6cf2,stroke-width:1px
  classDef contract fill:#0e1320,color:#7ea3ff,stroke:#7ea3ff,stroke-dasharray:4 3
  classDef infra fill:#202732,color:#cfd6e3,stroke:#566076
  classDef bus fill:#5a3fae,color:#fff,stroke:#3f2a85
  classDef ext fill:#2b3144,color:#cfd6e3,stroke:#566076,stroke-dasharray:6 3

  subgraph Clients
    direction LR
    SPA["Back-office SPA"]:::ext
    Vendor["Casino vendor (21G SOAP)"]:::ext
  end

  subgraph Host["Pam.Api (single deployable)"]
    direction TB
    Carter["Carter HTTP /v1/*"]:::api
    Soap["SoapCore /integrations/21GCasino/*"]:::api

    subgraph Modules
      direction LR
      Identity["Pam.Identity"]:::module
      Operators["Pam.Operators"]:::module
      Ingest["Pam.Ingest"]:::module
      Players["Pam.Players"]:::module
      Wallet["Pam.Wallet"]:::module
      Notifications["Pam.Notifications"]:::module
      Audit["Pam.Audit"]:::module
    end

    Contracts["Pam.&lt;X&gt;.Contracts<br/>(IQuery, IntegrationEvents)"]:::contract

    Shared["Pam.Shared.Messaging<br/>outbox + reconciler"]:::bus
    PartSvc["PartitionMaintenanceService<br/>(BackgroundService)"]:::bus
  end

  subgraph Infra["Infrastructure (docker-compose)"]
    direction LR
    SQL[("SQL Server<br/>schemas: identity, operators,<br/>ingest, messaging, audit, …")]:::infra
    Redis[("Redis<br/>cache + rate-limiter")]:::infra
    Rabbit[("RabbitMQ<br/>bus")]:::infra
    Mail[("Mailpit<br/>SMTP dev sink")]:::infra
    OTel[("Grafana LGTM<br/>traces/metrics/logs")]:::infra
  end

  SPA -->|Bearer / OIDC| Carter
  Vendor -->|SOAP envelope| Soap

  Carter --> Identity
  Carter --> Operators
  Carter --> Ingest
  Soap --> Ingest

  Identity --> Contracts
  Operators --> Contracts
  Ingest --> Contracts
  Players --> Contracts
  Wallet --> Contracts
  Notifications -. consume .-> Contracts
  Audit -. consume .-> Contracts

  Identity --> SQL
  Operators --> SQL
  Ingest --> SQL
  Players --> SQL
  Wallet --> SQL

  Identity -. domain event .-> Shared
  Ingest -. domain event .-> Shared
  Shared --> SQL
  Shared --> Rabbit
  Rabbit --> Notifications
  Rabbit --> Audit

  PartSvc -. daily .-> SQL

  Identity --> Redis
  Operators --> Redis
  Notifications --> Mail

  Host --> OTel
`;

type Feature = {
  title: string;
  to: string;
  icon: string;
  description: ReactNode;
};

const features: Feature[] = [
  {
    title: "Architecture",
    to: "/ARCHITECTURE",
    icon: "🏛️",
    description:
      "Modular monolith over SQL Server. Per-module pattern, contracts seam, embedded OpenIddict, outbox + reconciler.",
  },
  {
    title: "Authentication",
    to: "/AUTH",
    icon: "🔐",
    description:
      "OpenIddict + ASP.NET Identity. AuthZ Code + PKCE, refresh rotation, MFA, per-permission policies.",
  },
  {
    title: "Ingest",
    to: "/INGEST",
    icon: "🛰️",
    description:
      "Vendor-agnostic transaction intercept. 21G SOAP listener, idempotent persistence, strangler-fig phases.",
  },
  {
    title: "Core platform mapping",
    to: "/CORE_PLATFORM_MAPPING",
    icon: "🗺️",
    description:
      "How PAM modules map onto the CTO platform model: customer service, players, wallet, affiliates, gaming.",
  },
  {
    title: "Endpoints",
    to: "/ENDPOINTS",
    icon: "🧩",
    description:
      "Carter annotation chain. Scalar-friendly OpenAPI metadata: summary, description, schemas, every status code.",
  },
  {
    title: "Testing",
    to: "/TESTING",
    icon: "🧪",
    description:
      "xUnit v3, Testcontainers, NetArchTest. Architecture rules, integration harness, validator unit tests.",
  },
];

function FeatureCard({ feature }: { feature: Feature }) {
  return (
    <Link to={feature.to} className={clsx("col col--4", styles.featureCol)}>
      <div className={styles.featureCard}>
        <div className={styles.featureIcon}>{feature.icon}</div>
        <Heading as="h3" className={styles.featureTitle}>
          {feature.title}
        </Heading>
        <p className={styles.featureDescription}>{feature.description}</p>
      </div>
    </Link>
  );
}

function Hero() {
  const { siteConfig } = useDocusaurusContext();
  return (
    <header className={clsx("hero", styles.heroBanner)}>
      <div className="container">
        <Heading as="h1" className={clsx("hero__title", styles.heroTitle)}>
          {siteConfig.title}
        </Heading>
        <p className={clsx("hero__subtitle", styles.heroSubtitle)}>
          {siteConfig.tagline}
        </p>
        <div className={styles.buttons}>
          <Link className="button button--primary button--lg" to="/ARCHITECTURE">
            Architecture overview
          </Link>
          <Link className="button button--secondary button--lg" to="/LOCAL_DEV">
            Local development
          </Link>
        </div>
      </div>
    </header>
  );
}

export default function Home(): ReactNode {
  const { siteConfig } = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description="PAM — Player Admin Manager. Modular monolith for multi-brand iGaming back-office."
    >
      <Hero />
      <main>
        <section className={styles.diagramSection}>
          <div className="container">
            <Heading as="h2" className={styles.sectionTitle}>
              The platform at a glance
            </Heading>
            <p className={styles.sectionLede}>
              One deployable, schema-per-module on a single SQL Server. A
              bus-wide outbox + reconciler bridges every cross-module fact
              to RabbitMQ. Pages below dive into each piece.
            </p>
            <div className={styles.diagramFrame}>
              <Mermaid value={platformDiagram} />
            </div>
          </div>
        </section>
        <section className={styles.featuresSection}>
          <div className="container">
            <div className="row">
              {features.map((f) => (
                <FeatureCard key={f.to} feature={f} />
              ))}
            </div>
          </div>
        </section>
      </main>
    </Layout>
  );
}
