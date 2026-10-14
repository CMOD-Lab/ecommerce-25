# EcommerceWebApi — Deployment Guide

## Overview

This guide covers building, containerizing, and deploying the **EcommerceWebApi** ASP.NET Core 6.0 Web API to **AWS EKS (Elastic Kubernetes Service)**.

- **Technology**: ASP.NET Core 6.0 Web API
- **Container Runtime**: Docker
- **Orchestration**: Kubernetes (AWS EKS)
- **Application Port**: 3000
- **Health Endpoint**: `/health`

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Project Structure](#project-structure)
3. [Local Development with Docker Compose](#local-development-with-docker-compose)
4. [Build and Push Docker Image](#build-and-push-docker-image)
5. [AWS EKS Prerequisites](#aws-eks-prerequisites)
6. [EKS Cluster Setup](#eks-cluster-setup)
7. [Kubernetes Deployment](#kubernetes-deployment)
8. [Configuration Management](#configuration-management)
9. [Scaling and Management](#scaling-and-management)
10. [Troubleshooting](#troubleshooting)
11. [Security Considerations](#security-considerations)
12. [.NET-Specific Notes](#net-specific-notes)

---

## Prerequisites

### Local Development
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) 20.10+
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [Docker Compose](https://docs.docker.com/compose/install/) v2+

### AWS EKS Deployment
- [AWS CLI](https://aws.amazon.com/cli/) v2+ (configured with appropriate IAM permissions)
- [kubectl](https://kubernetes.io/docs/tasks/tools/) v1.24+
- [eksctl](https://eksctl.io/) (optional, for cluster creation)
- AWS IAM permissions: `eks:*`, `ecr:*`, `ec2:*`, `iam:PassRole`

---

## Project Structure

```
CMP1/
├── Dockerfile                  # Multi-stage Docker build
├── docker-compose.yml          # Local development compose file
├── .dockerignore               # Docker build exclusions
├── EcommerceWebApi.csproj      # .NET project file
├── Program.cs                  # Application entry point
├── appsettings.json            # Base configuration
├── appsettings.Development.json
├── kubernetes/
│   ├── namespace.yaml          # Kubernetes namespace
│   ├── deployment.yaml         # Deployment manifest
│   ├── service.yaml            # ClusterIP service
│   └── ingress.yaml            # AWS ALB ingress
├── scripts/
│   ├── build-push.sh           # Linux/macOS build & push
│   ├── build-push.bat          # Windows build & push
│   ├── deploy-image.sh         # Linux/macOS EKS deploy
│   └── deploy-image.bat        # Windows EKS deploy
└── docs/
    └── DEPLOYMENT.md           # This file
```

---

## Local Development with Docker Compose

### 1. Configure Environment Variables

Create a `.env` file in the project root:

```env
REDIS_CONNECTION_STRING=localhost:6379
SIGNALR_CORS_ORIGINS=http://localhost:3001
APPLICATION_SECRET=your-jwt-secret-here
```

### 2. Start the Application

```bash
docker compose up --build
```

The API will be available at: `http://localhost:3000`  
Swagger UI: `http://localhost:3000/swagger`  
Health check: `http://localhost:3000/health`

### 3. Stop the Application

```bash
docker compose down
```

### 4. View Logs

```bash
docker compose logs -f ecommercewebapi
```

---

## Build and Push Docker Image

### Linux / macOS

```bash
chmod +x scripts/build-push.sh
./scripts/build-push.sh
```

### Windows

```cmd
scripts\build-push.bat
```

The script will interactively prompt for:
1. **Image tag** (defaults to `latest`)
2. **Registry type**: AWS ECR or Docker Hub
3. **Registry credentials** based on selection

#### AWS ECR Example
```
Enter image tag [latest]: v1.0.0
Select container registry:
  1. AWS ECR
  2. Docker Hub
Enter choice [1]: 1
Enter AWS Region: us-east-1
Enter AWS Account ID: 123456789012
Enter ECR Repository name [ecommercewebapi]: ecommercewebapi
```

The script automatically:
- Creates the ECR repository if it does not exist
- Authenticates with ECR
- Builds and pushes the image

---

## AWS EKS Prerequisites

### 1. Configure AWS CLI

```bash
aws configure
# Enter: AWS Access Key ID, Secret Access Key, Region, Output format
```

### 2. Verify IAM Permissions

Your IAM user/role needs:
- `AmazonEKSClusterPolicy`
- `AmazonEKSWorkerNodePolicy`
- `AmazonEC2ContainerRegistryReadOnly`
- `AmazonEKS_CNI_Policy`

### 3. Install AWS Load Balancer Controller

The ingress manifest uses the AWS Load Balancer Controller. Install it on your cluster:

```bash
# Add the EKS chart repo
helm repo add eks https://aws.github.io/eks-charts
helm repo update

# Install the controller
helm install aws-load-balancer-controller eks/aws-load-balancer-controller \
  -n kube-system \
  --set clusterName=<YOUR_CLUSTER_NAME> \
  --set serviceAccountName=aws-load-balancer-controller
```

---

## EKS Cluster Setup

### Create a New Cluster (if needed)

```bash
eksctl create cluster \
  --name ecommercewebapi-cluster \
  --region us-east-1 \
  --nodegroup-name standard-workers \
  --node-type t3.medium \
  --nodes 2 \
  --nodes-min 1 \
  --nodes-max 4 \
  --managed
```

### Configure kubectl

```bash
aws eks update-kubeconfig --region us-east-1 --name ecommercewebapi-cluster
kubectl cluster-info
```

---

## Kubernetes Deployment

### Automated Deployment (Recommended)

#### Linux / macOS

```bash
chmod +x scripts/deploy-image.sh
./scripts/deploy-image.sh
```

#### Windows

```cmd
scripts\deploy-image.bat
```

The script prompts for:
- AWS Region and EKS Cluster Name
- Full Docker image URI (e.g., `123456789.dkr.ecr.us-east-1.amazonaws.com/ecommercewebapi:v1.0.0`)
- Application environment variables (Redis, CORS origins, JWT secret)

### Manual Deployment

If you prefer to deploy manually, update the placeholders in `kubernetes/deployment.yaml` first:

```bash
# Replace image URI
sed -i 's|{{IMAGE_URI}}|123456789.dkr.ecr.us-east-1.amazonaws.com/ecommercewebapi:v1.0.0|g' kubernetes/deployment.yaml

# Replace environment variables
sed -i 's|{{REDIS_CONNECTION_STRING}}|my-redis.cache.amazonaws.com:6379|g' kubernetes/deployment.yaml
sed -i 's|{{SIGNALR_CORS_ORIGINS}}|https://myapp.example.com|g' kubernetes/deployment.yaml
sed -i 's|{{APPLICATION_SECRET}}|my-jwt-secret|g' kubernetes/deployment.yaml

# Apply manifests
kubectl apply -f kubernetes/namespace.yaml
kubectl apply -f kubernetes/deployment.yaml
kubectl apply -f kubernetes/service.yaml
kubectl apply -f kubernetes/ingress.yaml
```

### Verify Deployment

```bash
# Check pod status
kubectl get pods -n ecommercewebapi

# Check all resources
kubectl get pods,svc,ingress -n ecommercewebapi

# View pod logs
kubectl logs -l app=ecommercewebapi -n ecommercewebapi --tail=100

# Describe deployment
kubectl describe deployment ecommercewebapi -n ecommercewebapi
```

### Access the Application

```bash
# Get ingress hostname (AWS ALB)
kubectl get ingress ecommercewebapi-ingress -n ecommercewebapi
```

Update your DNS or `/etc/hosts` to point `ecommercewebapi.example.com` to the ALB hostname.

---

## Configuration Management

### Environment Variables

| Variable | Description | Required | Default |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment | Yes | `Production` |
| `ASPNETCORE_HTTP_PORT` | Kestrel HTTP port | Yes | `3000` |
| `REDIS_CONNECTION_STRING` | Redis/ElastiCache endpoint | Yes | `localhost:6379` |
| `SIGNALR_CORS_ORIGINS` | Allowed CORS origins (comma-separated) | No | `http://localhost:3001` |
| `ApplicationSettings__Secret` | JWT signing secret | Yes | — |

### Using Kubernetes Secrets (Recommended for Production)

Instead of plain-text env vars, use Kubernetes Secrets for sensitive values:

```bash
kubectl create secret generic ecommercewebapi-secrets \
  --from-literal=APPLICATION_SECRET=my-jwt-secret \
  --from-literal=REDIS_CONNECTION_STRING=my-redis:6379 \
  -n ecommercewebapi
```

Then reference in `deployment.yaml`:

```yaml
env:
  - name: ApplicationSettings__Secret
    valueFrom:
      secretKeyRef:
        name: ecommercewebapi-secrets
        key: APPLICATION_SECRET
  - name: REDIS_CONNECTION_STRING
    valueFrom:
      secretKeyRef:
        name: ecommercewebapi-secrets
        key: REDIS_CONNECTION_STRING
```

---

## Scaling and Management

### Manual Scaling

```bash
kubectl scale deployment ecommercewebapi --replicas=4 -n ecommercewebapi
```

### Horizontal Pod Autoscaler (HPA)

```bash
kubectl autoscale deployment ecommercewebapi \
  --cpu-percent=70 \
  --min=2 \
  --max=10 \
  -n ecommercewebapi
```

### Rolling Update

```bash
# Update image
kubectl set image deployment/ecommercewebapi \
  ecommercewebapi=123456789.dkr.ecr.us-east-1.amazonaws.com/ecommercewebapi:v2.0.0 \
  -n ecommercewebapi

# Monitor rollout
kubectl rollout status deployment/ecommercewebapi -n ecommercewebapi
```

### Rollback

```bash
# Rollback to previous version
kubectl rollout undo deployment/ecommercewebapi -n ecommercewebapi

# Rollback to specific revision
kubectl rollout history deployment/ecommercewebapi -n ecommercewebapi
kubectl rollout undo deployment/ecommercewebapi --to-revision=2 -n ecommercewebapi
```

---

## Troubleshooting

### Pod Not Starting

```bash
# Check pod events
kubectl describe pod <pod-name> -n ecommercewebapi

# Check pod logs
kubectl logs <pod-name> -n ecommercewebapi

# Check previous container logs (if crashed)
kubectl logs <pod-name> -n ecommercewebapi --previous
```

### Common Issues

| Issue | Cause | Solution |
|---|---|---|
| `ImagePullBackOff` | ECR auth failure or wrong image URI | Verify ECR permissions and image URI |
| `CrashLoopBackOff` | App startup failure | Check logs; verify `REDIS_CONNECTION_STRING` is reachable |
| `Pending` pods | Insufficient cluster resources | Scale node group or reduce resource requests |
| Health probe failing | App not listening on port 3000 | Verify `ASPNETCORE_HTTP_PORT=3000` env var is set |
| Ingress not provisioning | ALB controller not installed | Install AWS Load Balancer Controller |

### Redis Connectivity

```bash
# Exec into pod to test Redis connectivity
kubectl exec -it <pod-name> -n ecommercewebapi -- /bin/sh
# Then test: nc -zv <redis-host> 6379
```

### View Application Logs

```bash
# Stream logs from all pods
kubectl logs -l app=ecommercewebapi -n ecommercewebapi -f

# Logs are also written to /app/Logs inside the container
kubectl exec -it <pod-name> -n ecommercewebapi -- ls /app/Logs
```

---

## Security Considerations

1. **Non-root container**: The Dockerfile runs the application as user `appuser` (UID 1001)
2. **Secrets management**: Use Kubernetes Secrets or AWS Secrets Manager for sensitive values
3. **JWT Secret**: Never commit `ApplicationSettings__Secret` to source control
4. **Network policies**: Consider adding Kubernetes NetworkPolicy to restrict pod-to-pod traffic
5. **HTTPS**: Configure TLS termination at the ALB level using ACM certificates:
   ```yaml
   annotations:
     alb.ingress.kubernetes.io/listen-ports: '[{"HTTPS": 443}]'
     alb.ingress.kubernetes.io/certificate-arn: arn:aws:acm:us-east-1:123456789:certificate/xxx
   ```
6. **CORS**: Set `SIGNALR_CORS_ORIGINS` to only your trusted frontend origins
7. **Image scanning**: Enable ECR image scanning on push

---

## .NET-Specific Notes

### Runtime Base Image

This project uses `mcr.microsoft.com/dotnet/runtime:6.0` as the explicit runtime base image (as specified). Note that for ASP.NET Core Web API applications, `mcr.microsoft.com/dotnet/aspnet:6.0` is typically recommended as it includes the ASP.NET Core runtime libraries. If you encounter runtime issues, consider switching to the aspnet image.

### Kestrel Port Configuration

The application uses `ASPNETCORE_HTTP_PORT` environment variable to configure the Kestrel port (default: 3000). This is set in `Program.cs`:

```csharp
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    var httpPort = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORT"), out var p) ? p : 3000;
    serverOptions.ListenAnyIP(httpPort);
});
```

### Health Checks

The application exposes `/health` via ASP.NET Core's built-in health check middleware:
```csharp
builder.Services.AddHealthChecks();
app.MapHealthChecks("/health");
```

Kubernetes liveness and readiness probes are configured to use this endpoint.

### Serilog File Logging

Logs are written to `/app/Logs/log-{date}.txt` inside the container. The Kubernetes deployment mounts an `emptyDir` volume at `/app/Logs`. For persistent log storage, consider:
- Mounting an EFS volume
- Using a log aggregation solution (CloudWatch, Datadog, ELK)
- Configuring Serilog to write to CloudWatch directly

### Redis (Amazon ElastiCache)

The application uses Redis for distributed caching (session state, JWT tokens, TOTP). In production on EKS, use **Amazon ElastiCache for Redis**:

```bash
# Set the connection string
REDIS_CONNECTION_STRING=my-cluster.abc123.cache.amazonaws.com:6379
```

Ensure the EKS worker nodes' security group allows outbound traffic to the ElastiCache security group on port 6379.

### SignalR

The application uses SignalR at `/notificationHub`. Ensure your ALB/ingress is configured to support WebSocket connections:

```yaml
annotations:
  alb.ingress.kubernetes.io/target-group-attributes: stickiness.enabled=true,stickiness.lb_cookie.duration_seconds=86400
```
