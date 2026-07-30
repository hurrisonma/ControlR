# GitHub Actions Workflows for ControlR

This repository uses GitHub Actions to build, test, and deploy ControlR.

## Available Workflows

### 1. Build and Deploy (Main Application)

The main workflow (`build-and-deploy.yml`) handles building the ControlR application and deploying it to various targets.

#### Triggering the Workflow

This workflow is manually triggered. To start it:

1. Go to the "Actions" tab in your repository
2. Select "Build and Deploy" from the list of workflows
3. Click "Run workflow"
4. Choose your deployment target:
   - **preview**: Deploys to Docker Hub with the `preview` tag
   - **production**: Deploys to Docker Hub with the `latest` tag
   - **github_release**: Creates a GitHub release with artifacts
5. Optionally specify a custom version number
6. Choose whether to create a GitHub Release

#### Workflow Steps

1. Builds the ControlR application using the Build.ps1 script
2. Runs tests
3. Signs the executables (if code signing certificate is available)
4. Creates artifacts
5. Deploys to the selected target

### 2. Relay Server

The relay server workflow (`relay-server.yml`) handles building and deploying the WebSocket Relay server.

#### Triggering the Workflow

This workflow can be triggered:

- Automatically on push to the `main` branch
- Manually with workflow dispatch

For manual triggering:

1. Go to the "Actions" tab in your repository
2. Select "Relay Server" from the list of workflows
3. Click "Run workflow"
4. Choose your deployment target:
   - **preview**: Deploys to Docker Hub with the `preview` tag
   - **production**: Deploys to Docker Hub with the `latest` tag

#### Workflow Steps

1. Builds the WebSocket Relay Server
2. Creates and pushes Docker images with appropriate tags

## Required Secrets

For these workflows to function properly, you need to set up the following repository secrets:

- `DOCKER_USERNAME`: Your Docker Hub username
- `DOCKER_PAT`: Your Docker Hub access token
- `CERTIFICATE_THUMBPRINT`: Thumbprint of the code signing certificate (optional)
- `SIGNTOOL_BINARY`: Base64-encoded SignTool executable (optional)

## GitHub Release Assets

When creating a GitHub Release, the following assets are included:

- `ControlR.Server.[version].zip`: Server application
- `docker-compose.yaml`: Docker Compose file

## Docker Images

Docker images are published to Docker Hub:

- `bitbound/controlr:preview` - Preview/development version
- `bitbound/controlr:latest` - Production version
- `bitbound/controlr:[version]` - Specific version
- `bitbound/controlr-relay:preview` - Preview relay server
- `bitbound/controlr-relay:latest` - Production relay server
- `bitbound/controlr-relay:[version]` - Specific version of relay server

## StableStation GHCR Image

The `publish-stablestation-ghcr.yml` workflow is a fork-specific, manually triggered
publisher for the isolated StableStation prototype.

- It builds `linux/amd64` from the exact selected Git revision.
- It requires an explicit four-component numeric ControlR version.
- It publishes only the immutable
  `ghcr.io/hurrisonma/controlr-server:sha-<full-commit-sha>` tag.
- The workflow summary prints the registry digest that StableStation must pin.
- It uses the repository `GITHUB_TOKEN`; no Docker Hub credentials are required.
- It publishes SBOM and provenance attestations with the image.

This server image is not the complete StableStation Remote Assistance release. In
particular, it does not publish signed Windows Agent/DesktopClient packages and does
not lift the remaining capability, Local Gate, generation fencing, or session
revocation blockers.
