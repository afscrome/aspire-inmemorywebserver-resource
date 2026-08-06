# SLSA Pipeline Testing & Development Guide

This guide provides instructions for testing, developing, and troubleshooting the SLSA L3 GitHub Actions pipeline for the Aspire InMemoryWebServer project.

## Quick Start

### Testing the Pipeline Locally

The workflows use GitHub Actions, so local testing is limited. However, you can simulate the build steps:

```bash
# 1. Checkout your feature branch
git switch pipeline
git pull origin pipeline

# 2. Restore dependencies
dotnet restore

# 3. Build in Release mode (same as CI)
dotnet build --configuration Release --no-restore

# 4. Build frontend
cd app/Frontend
npm install
npm run build
cd ../..

# 5. Run tests (same assertions as CI)
dotnet test --configuration Release --no-build --verbosity detailed
```

### Testing the Workflow Definition

Validate workflow YAML syntax before pushing:

```bash
# Using GitHub's CLI tool to validate workflows
gh workflow list

# Visual inspection - check for:
# - Valid YAML syntax
# - Valid GitHub Actions action references
# - Correct permission declarations
# - Output variable references match between jobs
```

---

## Making Changes to Workflows

### 1. Editing Workflow Files

Workflow files are located in `.github/workflows/`:

- `pr-validation.yml` - Main PR validation pipeline
- `slsa-build.yml` - SLSA provenance builder (reusable)
- `slsa-verify.yml` - Provenance verification workflow

### 2. Testing Changes

**Option A: Create a test branch and push**

```bash
git switch -c test/workflow-changes
# Edit workflow files
git add .github/workflows/
git commit -m "chore: update SLSA workflow"
git push origin test/workflow-changes

# Create PR to trigger pr-validation.yml
# Observe in GitHub Actions tab
```

**Option B: Use workflow_dispatch for manual testing**

Add to any workflow:

```yaml
on:
  workflow_dispatch:
    inputs:
      description:
        description: 'Manual trigger reason'
        required: false
```

Then trigger via:

```bash
gh workflow run pr-validation.yml --ref pipeline
```

### 3. Common Modifications

#### Adjust Artifact Retention

In `pr-validation.yml`, find the `upload-artifact` steps:

```yaml
- uses: actions/upload-artifact@v4
  with:
    name: build-artifacts
    path: ${{ steps.paths.outputs.build-output-dir }}
    retention-days: 5  # Change this
```

#### Add Additional Build Steps

Insert new steps in the `build` job:

```yaml
- name: My Custom Step
  run: |
    echo "Custom logic here"
    # Build outputs are in: ${{ steps.paths.outputs.build-output-dir }}
```

#### Change Test Configuration

Modify the test invocation:

```yaml
- name: Run tests
  run: dotnet test \
    --configuration Release \
    --no-build \
    --logger trx \
    --collect:"XPlat Code Coverage" \
    --verbosity detailed  # Add more verbosity
```

#### Modify Node.js/Frontend Build

In `pr-validation.yml` build job:

```yaml
- name: Setup Node.js for frontend
  uses: actions/setup-node@v4
  with:
    node-version: '20'  # Change version if needed
    cache: 'npm'
    cache-dependency-path: 'app/Frontend/package-lock.json'
```

---

## Troubleshooting

### Workflow Not Triggering

**Problem**: PR opened but workflow doesn't start

**Solutions**:
1. Check if PR is against the correct branch (`main` or `pipeline`)
2. Verify workflow file is valid YAML:
   ```bash
   # Use online YAML validator or GitHub CLI
   gh workflow view pr-validation.yml
   ```
3. Ensure `.github/workflows/` directory exists with proper permissions
4. Check repository settings → Actions → Workflows permissions are enabled

### Build Fails

**Problem**: Workflow runs but build job fails

**Debug steps**:
```bash
# 1. Check logs in GitHub Actions tab
# 2. Reproduce locally
dotnet build --configuration Release

# 3. Check for missing dependencies
dotnet nuget list source  # Verify NuGet feeds

# 4. Clear cache and retry
rm -rf ~/.nuget/packages
dotnet clean
dotnet build
```

### Tests Fail in CI but Pass Locally

**Likely causes**:
1. Different .NET versions: Check `global.json` matches runner
   ```bash
   # Runner uses: dotnet-version: '10.0.x'
   cat global.json
   ```
2. Environment variables differ: Check workflow passes required vars
3. Path separators: Windows vs Linux - workflows use Linux
4. Timing issues: Flaky tests - add retry logic or wait times

**Fix**:
```yaml
- name: Run tests with retry
  uses: nick-invision/retry@v3
  with:
    timeout_minutes: 10
    max_attempts: 3
    command: dotnet test --configuration Release --no-build
```

### SLSA Provenance Not Generated

**Problem**: Build succeeds but no `.intoto.jsonl` artifact

**Causes**:
1. Missing `id-token: write` permission
2. `slsa-framework/slsa-github-generator` action failed silently
3. No artifacts passed to SLSA generator

**Fix**:
```yaml
permissions:
  id-token: write  # MUST be set
  contents: read
  actions: read
```

Check SLSA generator step output in logs.

### Artifact Upload Fails

**Problem**: "Failed to upload artifact"

**Solutions**:
```yaml
# 1. Ensure path exists before upload
- name: Check artifact directory exists
  run: ls -lah ${{ steps.paths.outputs.build-output-dir }}

# 2. Use correct paths (no ~ or $HOME)
- uses: actions/upload-artifact@v4
  with:
    path: ${{ github.workspace }}/build-output  # Use absolute

# 3. Reduce artifact size if too large
- name: Compress artifacts
  run: tar -czf artifacts.tar.gz ${{ steps.paths.outputs.build-output-dir }}
```

---

## Performance Optimization

### Reducing Build Time

#### 1. Enable Dependency Caching

Already enabled in workflow:

```yaml
- uses: actions/setup-dotnet@v4
  with:
    cache: true
    cache-dependency-path: '**/packages.lock.json'
```

To use package lock file:

```bash
dotnet nuget locals all --clear
dotnet list package --outdated
# Commit packages.lock.json for reproducibility
```

#### 2. Parallel Test Execution

Modify test step:

```yaml
- name: Run tests in parallel
  run: dotnet test \
    --configuration Release \
    --no-build \
    --parallel 4 \
    --logger trx
```

#### 3. Reduce Frontend Build Time

```yaml
- name: Build frontend (optimized)
  working-directory: app/Frontend
  run: |
    npm ci  # Use ci instead of install for reproducibility
    npm run build  # Make sure esbuild or similar minification is enabled
```

### Cost Optimization

- **Artifact retention**: Reduce from 5 days if not needed
- **Test runs**: Skip verification job on certain conditions
- **Job parallelization**: Run independent jobs simultaneously

---

## SLSA Compliance Validation

### Verify Workflow Meets SLSA L3

Checklist:

- [ ] Permissions include `id-token: write`
- [ ] Uses GitHub-hosted runners only (not self-hosted)
- [ ] Build job is isolated (no secrets passed as environment variables)
- [ ] SLSA framework official generator workflow is used
- [ ] Artifacts have checksums recorded
- [ ] Provenance includes repository URL and commit SHA
- [ ] All inputs to SLSA generator come from trusted sources

### Check Provenance Attestation

After workflow completes:

```bash
# Download provenance
gh run download <RUN_ID> -n <attestation-name>

# Inspect structure
cat *.intoto.jsonl | jq .

# Expected fields:
# - payloadType: "application/vnd.in-toto+json"
# - signatures: [{ keyid, sig }]
# - payload: base64-encoded predicate with material and byproducts
```

---

## Local Development Workflow

### Setup for Development

```bash
# 1. Clone repo and switch to pipeline branch
git clone https://github.com/afscrome/aspire-inmemorywebserver-resource.git
cd Aspire.InMemoryWebServer
git switch pipeline

# 2. Install tools locally (optional)
# SLSA verifier
wget https://github.com/slsa-framework/slsa-verifier/releases/download/v2.5.1/slsa-verifier-linux-amd64
mv slsa-verifier-linux-amd64 /usr/local/bin/slsa-verifier

# 3. Test build locally
dotnet build -c Release
dotnet test -c Release
```

### Commit & Push Pattern

```bash
# Make workflow changes
git add .github/workflows/

# Commit with descriptive message
git commit -m "feat: add SLSA L3 provenance signing

- Use SLSA generic generator for artifact provenance
- Sign with Sigstore/Fulcio keyless signing
- Log provenance to Rekor transparency log
- Verify on each PR with slsa-verifier tool"

# Push to branch
git push origin pipeline

# Create PR and observe workflow
gh pr create --title "SLSA L3 Pipeline Implementation"
```

---

## Reference Information

### Key Files

| File | Purpose |
|------|---------|
| `.github/workflows/pr-validation.yml` | Main workflow triggered on PRs |
| `.github/workflows/slsa-build.yml` | Reusable job generating provenance subject |
| `.github/workflows/slsa-verify.yml` | Standalone verification workflow |
| `.github/SLSA_L3_PIPELINE.md` | High-level documentation |
| `global.json` | .NET version specification |
| `Directory.Build.props` | MSBuild properties (IsPackable, etc.) |

### External References

- [SLSA GitHub Generator](https://github.com/slsa-framework/slsa-github-generator)
- [Sigstore Documentation](https://docs.sigstore.dev)
- [GitHub OIDC Token Reference](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect)
- [Actions/upload-artifact Documentation](https://github.com/actions/upload-artifact)

### Useful Commands

```bash
# View all workflows in repo
gh workflow list

# Check specific workflow status
gh workflow view pr-validation.yml

# View latest workflow run
gh run list --workflow pr-validation.yml -L 1

# Download artifacts from a run
gh run download <RUN_ID> -n build-artifacts

# Trigger manual workflow
gh workflow run slsa-verify.yml --ref pipeline

# Check workflow permissions
cat .github/workflows/pr-validation.yml | grep -A 5 "permissions:"
```

---

## Next Steps

1. **Test the workflow**: Create a test PR and monitor the actions run
2. **Verify artifacts**: Download and inspect provenance attestations
3. **Document locally**: Create internal runbooks for your team
4. **Extend pipeline**: Plan Phase 2 (release automation) based on findings
5. **Monitor compliance**: Ensure all future PRs generate valid SLSA provenance

For issues or enhancements, open discussions or create issues in the repository.
