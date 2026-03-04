# SLSA L3 GitHub Actions Pipeline Documentation

This project implements a **SLSA Level 3** compliant GitHub Actions pipeline for secure software supply chain assurance.

## Table of Contents

- [Overview](#overview)
- [What is SLSA?](#what-is-slsa)
- [Architecture](#architecture)
- [Workflows](#workflows)
- [Verification](#verification)
- [Future Enhancements](#future-enhancements)

---

## Overview

This project uses the [SLSA Framework](https://github.com/slsa-framework/slsa-github-generator) Generic Generator to provide cryptographically verifiable build provenance for all artifacts produced by the project's GitHub Actions pipelines.

**SLSA Compliance Level**: L3  
**Builder**: GitHub Actions + Sigstore/Fulcio (Keyless Signing)  
**Provenance Format**: in-toto attestation format (intoto.jsonl)

### Key Features

✅ **Unforgeable Provenance** - Cryptographic signing using GitHub OIDC + Sigstore  
✅ **Build Isolation** - Each build runs in a fresh Ubuntu runner VM  
✅ **Transparent Logging** - Provenance logged to public Rekor transparency log  
✅ **Hosted Builds** - GitHub Actions (trusted Google/GitHub infrastructure)  
✅ **No Secrets Management** - Uses ephemeral OIDC tokens instead of long-lived keys  

---

## What is SLSA?

SLSA ("Supply-chain Levels for Software Artifacts") is a framework for securing the software supply chain by ensuring artifacts can be traced back to their source and verifying their provenance.

**SLSA Levels:**

| Level | Description | Use Case |
|-------|-------------|----------|
| L0 | No provenance | Development/testing |
| L1 | Documentation only | Continuous integration present |
| L2 | Signed provenance | Verifiable build system |
| **L3** | **Isolated builds + signed provenance** | **Production releases** ✓ |
| L4 | Hardened builds + hermetic builds | Maximum security |

This project targets **L3**, which provides:
- Provenance generation in authenticated environments
- Isolated build jobs with no cross-contamination
- Cryptographically signed attestations
- Verification against Sigstore root CA

---

## Architecture

### Workflow Orchestration

```
┌─────────────────────────────────────────────────────────────┐
│ GitHub Push/PR Event                                        │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────────┐
│ pr-validation.yml (Main Workflow)                           │
│ ├─ Triggers on: PR opened/synchronize/reopened             │
│ └─ Permissions: id-token=write, contents=read              │
└──────────────────────┬──────────────────────────────────────┘
                       │
        ┌──────────────┴──────────────┬──────────────────────┐
        │                             │                      │
        ▼                             ▼                      ▼
   ┌─────────┐                  ┌──────────┐          ┌──────────┐
   │  Build  │────────▶         │ SLSA     │──────▶   │ SLSA     │
   │  & Test │                  │ Builder  │          │ Generator│
   │   Job   │                  │(Reusable)│          │ (Official)
   └─────────┘                  └──────────┘          └──────────┘
        │                             │                      │
        │ Artifacts                   │ Provenance Subject   │
        │ Test Results                │ Checksums/Metadata   │
        │                             │                      │
        └─────────────────────────────┬──────────────────────┘
                                      │
                                      ▼
                            ┌──────────────────────┐
                            │  Signed Provenance   │
                            │ *.intoto.jsonl file  │
                            └──────────────────────┘
                                      │
                    ┌─────────────────┴─────────────────┐
                    │                                   │
                    ▼                                   ▼
          ┌──────────────────┐              ┌──────────────────┐
          │ GitHub Artifacts │              │ Rekor Ledger     │
          │ (Retained 5 days)│              │ (Public Log)     │
          └──────────────────┘              └──────────────────┘
```

---

## Workflows

### 1. `pr-validation.yml` (Main)

**Trigger**: Pull Request events (opened, synchronize, reopened)  
**Purpose**: Validate each PR with full build, test, and SLSA provenance generation

**Jobs**:

#### `build` Job
- **Runs on**: `ubuntu-latest`
- **Steps**:
  1. Checkout code with full Git history
  2. Setup .NET 10 SDK
  3. Setup Node.js 20 for frontend build
  4. Restore NuGet dependencies
  5. Build solution in Release configuration
  6. Build frontend with Vite
  7. Run tests (fail on error - **required**)
  8. Collect build artifacts (DLLs, packages, frontend dist)
  9. Generate BUILD_METADATA.json with build context
  10. Upload artifacts to GitHub Actions (5-day retention)
  11. Upload test results as separate artifact

**Outputs**: 
- `artifact-id`: GitHub Artifacts artifact ID
- `build-output-dir`: Path containing build artifacts

#### `slsa-builder` Job
- **Runs on**: `ubuntu-latest`
- **Calls**: Reusable workflow `slsa-build.yml`
- **Purpose**: Generate provenance subject from build artifacts
- **Outputs**: Base64-encoded provenance subject for SLSA framework

#### `slsa-provenance` Job
- **Calls**: Official SLSA framework workflow  
  `slsa-framework/slsa-github-generator/.github/workflows/generator_generic_slsa3.yml@v2.0.0`
- **Purpose**: Sign provenance attestation using GitHub OIDC + Sigstore
- **Inputs**: Base64-encoded artifact subjects from builder job
- **Outputs**: Signed `.intoto.jsonl` provenance attestation
- **Side effects**:
  - Uploads attestation to GitHub Artifacts
  - Logs entry to public Rekor transparency log
  - Generates GitHub workflow summary with attestation details

#### `verification` Job
- **Purpose**: Demonstrate SLSA verifier tool usage
- **Steps**:
  1. Download build artifacts
  2. Download provenance attestation
  3. Install slsa-verifier CLI tool
  4. Display provenance reference information

---

### 2. `slsa-build.yml` (Reusable)

**Trigger**: Called from `pr-validation.yml` via `uses:`  
**Purpose**: Generate SLSA provenance subject from artifacts

**Inputs**:
- `artifact-name` (required): Name of artifact uploaded by build job

**Process**:
1. Download build artifacts from previous job
2. Generate SHA256 checksums for all files
3. Create JSON provenance subject containing:
   - File names, sizes, and checksums
   - Repository metadata (commit, branch, workflow)
   - Build context (timestamp, actor, run ID)
4. Encode subject as base64 for SLSA framework
5. Upload provenance metadata artifact (5-day retention)

**Outputs**:
- `subject`: Base64-encoded provenance subject

---

### 3. `slsa-verify.yml` (Standalone)

**Trigger**: 
- `workflow_run`: Automatically after `pr-validation.yml` completes
- `workflow_dispatch`: Manual trigger for verification testing

**Purpose**: Verify SLSA provenance attestations and demonstrate verification process

**Steps**:
1. Download provenance artifacts
2. Install SLSA verifier tool (v2.5.1)
3. Display artifact contents and metadata
4. Show build metadata JSON
5. Provide verification commands for local use
6. Generate GitHub step summary with verification details

---

## Permissions

### Required GitHub Actions Permissions

```yaml
permissions:
  id-token: write      # Required for OIDC token generation with Sigstore
  contents: read       # Required for repository checkout
  actions: read        # Required for workflow metadata access
```

**Why these permissions?**

- **`id-token: write`**: GitHub Actions issues OpenID Connect (OIDC) tokens that are sent to Sigstore's Fulcio service. This enables keyless signing without storing long-lived secrets.
- **`contents: read`**: Allows checkout of repository code.
- **`actions: read`**: Allows reading workflow metadata for including in provenance attestation.

---

## Verification

### Automated Verification (GitHub Actions)

The `slsa-verify.yml` workflow automatically verifies every build:

```bash
# Manually trigger verification workflow
gh workflow run slsa-verify.yml --ref main
```

### Local Verification

To verify artifacts locally, install `slsa-verifier` and run:

```bash
# Download artifacts from GitHub
gh run download <run-id> -n build-artifacts

# Download provenance attestation
gh run download <run-id> -n <attestation-artifact>

# Verify artifact against provenance
slsa-verifier verify-artifact <artifact-file> \
  --provenance-path <attestation-file>.intoto.jsonl \
  --source-uri github.com/afscrome/aspire-inmemorywebserver-resource \
  --branch main
```

### Rekor Transparency Log

All SLSA L3 provenance entries are logged to the public [Rekor](https://rekor.sigstore.dev) transparency log.

To query Rekor:

```bash
# Install rekor-cli
go install github.com/sigstore/rekor/cmd/rekor-cli@latest

# Search for entries by repository
rekor-cli search --artifact-file <artifact> \
  --entry-type intoto
```

---

## SLSA Compliance Details

### SLSA L3 Requirements ✓

| Requirement | Implementation | Status |
|-------------|-----------------|--------|
| **Unforgeable Provenance** | OIDC token + Sigstore/Fulcio signing | ✓ |
| **Authentic Provenance** | Signed with root CA, verifiable via Sigstore | ✓ |
| **Provenance Format** | in-toto v0.2+ (.intoto.jsonl) | ✓ |
| **Build Isolation** | GitHub Actions ephemeral runners | ✓ |
| **Hosted Platform** | GitHub-hosted Ubuntu runners (not self-hosted) | ✓ |
| **Transitive Trust** | SLSA framework trusted builder pattern | ✓ |
| **Reliable Reporting** | Provenance includes repo, commit, workflow path | ✓ |
| **Artifact Tracking** | SHA256 checksums for all outputs | ✓ |

### Non-Requirements Met

- **No Long-Term Secrets**: Uses ephemeral OIDC tokens + keyless signing
- **Transparent Logging**: Automatic Rekor integration enabled
- **Consistent Versioning**: Build metadata captures exact state

---

## Artifact Retention

- **Build artifacts**: 5 days (GitHub Actions default)
- **Test results**: 5 days
- **Provenance attestations**: 5 days
- **Rekor entries**: Permanent (immutable log)

To modify retention, change the `retention-days` parameter in workflow:

```yaml
- uses: actions/upload-artifact@v4
  with:
    name: build-artifacts
    path: ./
    retention-days: 30  # Change as needed
```

---

## Future Enhancements

### Phase 2: Release Automation

Create a release workflow that:
- Triggers on Git tags (v*.*.*)
- Packages artifacts with SLSA provenance
- Publishes to NuGet.org (with trusted publisher OIDC)
- Publishes to GitHub Releases with attestations
- Logs provenance to Rekor

```yaml
# Planned: release.yml
on:
  push:
    tags:
      - 'v*.*.*'
```

### Phase 3: Container Image Supply Chain

Add container image SLSA provenance:
- Build container images in isolated workflow
- Use SLSA container builder
- Sign images with cosign + OpenID Connect
- Store provenance in OCI artifact registry

### Phase 4: Verification & Documentation

- Add SBOM (Software Bill of Materials) generation
- Integrate vulnerability scanning
- Create graphical supply chain attestation report
- Publish verification instructions to project website

---

## References

- [SLSA Framework](https://slsa.dev)
- [SLSA GitHub Generator](https://github.com/slsa-framework/slsa-github-generator)
- [Sigstore Documentation](https://docs.sigstore.dev)
- [in-toto Attestation Format](https://github.com/in-toto/attestation)
- [GitHub OpenID Connect](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)

---

## Support & Questions

For issues or questions about the SLSA pipeline:

1. Check GitHub Actions run logs for detailed error messages
2. Review SLSA framework documentation
3. Consult Sigstore community resources
4. File issues in the project repository
