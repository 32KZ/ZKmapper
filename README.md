# ZKMapper

ZKMapper is a **LinkedIn company mapping automation tool** designed to discover and structure professional contact data at scale.

The project focuses on transforming manual prospect research into a **repeatable, automated mapping workflow** that extracts relevant contacts from LinkedIn company pages and outputs structured datasets ready for outreach or analysis.

ZKMapper is built with **C# (.NET)** and uses **Playwright** for browser automation and **Spectre.Console** for an interactive CLI interface.

Playwright allows browser sessions to be saved and reused so the application can run without repeatedly logging in, which improves speed and reliability. :contentReference[oaicite:0]{index=0}

Spectre.Console enables modern interactive command-line applications with arrow-key navigation and rich UI components. :contentReference[oaicite:1]{index=1}

---

# IMPORTANT Security Notice

ZKMapper uses saved browser session state to maintain authentication.

These session files may contain sensitive cookies and authentication data.

They must **never be committed to public repositories**. If this data IS committed, somone could spoof your session. ensure that the sessions folder is in git ignore. same with the CSV files, as that is personal data.

---

# Project Goals

The purpose of ZKMapper is to automate the process of identifying key personnel within companies and generating structured contact datasets.

Typical use cases include:

- Sales prospect mapping
- Market intelligence
- Organizational research
- Talent scouting
- Contact discovery workflows


---

# Core Features

## Company Mapping Engine

ZKMapper can automatically explore LinkedIn company pages and extract discoverable employee profiles.

Capabilities include:

- Company People page navigation
- Job title keyword filtering
- Country / region filtering
- Automatic result scrolling
- Profile discovery queue


---

## Automated Profile Discovery

The mapper identifies profile "hero cards" on LinkedIn search results and extracts profile links for further processing.

Collected data includes:

- Full Name
- Profile URL
- Current headline
- Current role(s)


---

## Profile Extraction System

Each discovered profile is opened in a dedicated browser tab where relevant information is parsed and structured.

Extracted fields:

- Name
- Headline
- Current roles
- LinkedIn profile URL


---

## Email Pattern Generation

The system generates probable corporate email addresses based on name and company domain.

Supported patterns include:

- `firstname.lastname@domain`
- `firstinitial.lastname@domain`
- `firstnamelastname@domain`


---

## CSV Export Pipeline

All discovered contacts are written incrementally to CSV output files.

This allows long mapping runs to continue safely without data loss.

Example output:

```
Name,Title,ProfileURL,Email,EmailAlt1,EmailAlt2
Jane Doe,HR Director,linkedin.com/in/janedoe,jane.doe@company.com,jdoe@company.com
```


---

# Automation Architecture

ZKMapper uses a service-oriented architecture.

Core execution flow:

```
MapperApplication
    └── MenuService
        └── LinkedInQueryService
            └── BrowserManager
                └── PlaywrightContextFactory
```

Extraction pipeline:

```
ProfileExtractionService
    ├── EmailGenerationService
    └── CsvWriterService
```

Supporting services include:

- RetryService
- ScrollExhaustionService
- HumanDelayService
- SessionStateManager
- PlaywrightDiagnostics


---

# CLI Interface

ZKMapper provides an interactive command-line interface built with **Spectre.Console**.

Features include:

- Arrow-key navigable menus
- Interactive prompts
- Structured tables
- Progress indicators
- Mapping statistics display


---

# Authentication Handling

ZKMapper authenticates to LinkedIn using Playwright and stores the browser session state locally.

The session file contains cookies and storage data that allow the browser context to start already authenticated, avoiding repeated login flows. :contentReference[oaicite:2]{index=2}

Sensitive session files are excluded from version control via `.gitignore`.


---

# Input System

Batch mapping can be performed using input files.

Example input format:

```
Company|Domain|LinkedInURL|Country|Keywords
```

Example entry:

```
pepsico|pepsoco.com|https://linkedin.com/company/pepsico|germany|HR,L&D
```

This enables automated mapping runs across multiple organizations.


---

# Logging and Diagnostics

ZKMapper includes detailed logging using **Serilog**.

The application separates:

- User interface console
- Diagnostic logging console

Logs include:

- navigation steps
- selector matches
- retry attempts
- profile extraction results
- browser diagnostics


---

# Future Features

The following features represent the **long-term roadmap** for ZKMapper.

These capabilities will transform the mapper into a fully scalable data-collection system.

---

## Anti-Bot Behavior Simulation

To reduce detection risk, the mapper will simulate natural browsing patterns.

Examples:

- human-like delays
- randomized scroll behavior
- realistic interaction pacing
- retry and fallback navigation


---

## Advanced Contact Enrichment

Future enrichment possibilities include:

- inferred department classification
- role seniority detection
- decision-maker tagging
- contact prioritization scoring


---

# Technology Stack

Core technologies used in this project:

- **C# (.NET)**
- **Playwright**
- **Spectre.Console**
- **Serilog**
- **CsvHelper**
- **Polly** (retry policies)


---

# Status

ZKMapper is currently under active development.

Core mapping infrastructure is operational, with ongoing work focused on:

- selector stability
- profile extraction reliability
- CLI usability improvements


---

# License

MIT License


---

# Author

32KZ
