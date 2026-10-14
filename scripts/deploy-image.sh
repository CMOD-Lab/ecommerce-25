#!/bin/bash
set -e
set -o pipefail

# =============================================================================
# deploy-image.sh - Deploy EcommerceWebApi to AWS EKS
# =============================================================================

APP_NAME="ecommercewebapi"
NAMESPACE="ecommercewebapi"
K8S_DIR="kubernetes"

echo "=============================================="
echo "  EcommerceWebApi - Deploy to AWS EKS"
echo "=============================================="
echo ""

# ---- Collect deployment inputs ----
read -rp "Enter AWS Region (e.g. us-east-1): " AWS_REGION
if [ -z "$AWS_REGION" ]; then
  echo "AWS Region is required. Exiting."
  exit 1
fi

read -rp "Enter EKS Cluster Name: " CLUSTER_NAME
if [ -z "$CLUSTER_NAME" ]; then
  echo "EKS Cluster Name is required. Exiting."
  exit 1
fi

read -rp "Enter full Docker image URI (e.g. 123456789.dkr.ecr.us-east-1.amazonaws.com/ecommercewebapi:latest): " IMAGE_URI
if [ -z "$IMAGE_URI" ]; then
  echo "Docker image URI is required. Exiting."
  exit 1
fi

echo ""
echo "--- Application Environment Variables ---"
echo "Press Enter to skip any variable (placeholder will remain in manifest)."
echo ""

read -rp "Enter REDIS_CONNECTION_STRING (e.g. my-redis.cache.amazonaws.com:6379): " REDIS_CONNECTION_STRING
read -rp "Enter SIGNALR_CORS_ORIGINS (comma-separated, e.g. https://myapp.example.com): " SIGNALR_CORS_ORIGINS
read -rsp "Enter APPLICATION_SECRET (JWT signing secret): " APPLICATION_SECRET
echo ""

# ---- Configure kubectl for EKS ----
echo ""
echo "Configuring kubectl for EKS cluster: $CLUSTER_NAME in $AWS_REGION..."
aws eks update-kubeconfig --region "$AWS_REGION" --name "$CLUSTER_NAME"

echo "Verifying cluster connectivity..."
kubectl cluster-info || { echo "Cannot connect to EKS cluster. Exiting."; exit 1; }

# ---- Patch manifests with actual values ----
echo ""
echo "Updating Kubernetes manifests with deployment values..."

# Work on copies to avoid modifying originals
cp -r "$K8S_DIR" /tmp/k8s-deploy-$$
DEPLOY_DIR="/tmp/k8s-deploy-$$"

# Replace IMAGE_URI placeholder
sed -i "s|{{IMAGE_URI}}|${IMAGE_URI}|g" "$DEPLOY_DIR/deployment.yaml"

# Replace environment variable placeholders
if [ -n "$REDIS_CONNECTION_STRING" ]; then
  sed -i "s|{{REDIS_CONNECTION_STRING}}|${REDIS_CONNECTION_STRING}|g" "$DEPLOY_DIR/deployment.yaml"
fi

if [ -n "$SIGNALR_CORS_ORIGINS" ]; then
  sed -i "s|{{SIGNALR_CORS_ORIGINS}}|${SIGNALR_CORS_ORIGINS}|g" "$DEPLOY_DIR/deployment.yaml"
fi

if [ -n "$APPLICATION_SECRET" ]; then
  sed -i "s|{{APPLICATION_SECRET}}|${APPLICATION_SECRET}|g" "$DEPLOY_DIR/deployment.yaml"
fi

# ---- Apply manifests in order ----
echo ""
echo "Applying Kubernetes manifests..."

echo "[1/4] Applying namespace..."
kubectl apply -f "$DEPLOY_DIR/namespace.yaml"

echo "[2/4] Applying deployment..."
kubectl apply -f "$DEPLOY_DIR/deployment.yaml"

echo "[3/4] Applying service..."
kubectl apply -f "$DEPLOY_DIR/service.yaml"

echo "[4/4] Applying ingress..."
kubectl apply -f "$DEPLOY_DIR/ingress.yaml"

# ---- Wait for rollout ----
echo ""
echo "Waiting for deployment rollout to complete..."
kubectl rollout status deployment/"$APP_NAME" -n "$NAMESPACE" --timeout=300s

# ---- Verify resources ----
echo ""
echo "Verifying deployed resources..."
kubectl get pods,svc,ingress -n "$NAMESPACE"

# ---- Display access URL ----
echo ""
echo "Fetching application ingress URL..."
INGRESS_HOST=$(kubectl get ingress "${APP_NAME}-ingress" -n "$NAMESPACE" -o jsonpath='{.status.loadBalancer.ingress[0].hostname}' 2>/dev/null || echo "pending")
if [ "$INGRESS_HOST" != "pending" ] && [ -n "$INGRESS_HOST" ]; then
  echo "Application URL: http://${INGRESS_HOST}"
else
  echo "Ingress hostname is still provisioning. Run the following to check:"
  echo "  kubectl get ingress ${APP_NAME}-ingress -n ${NAMESPACE}"
fi

# ---- Cleanup temp files ----
rm -rf "$DEPLOY_DIR"

echo ""
echo "=============================================="
echo "  Deployment Complete!"
echo "  Namespace : $NAMESPACE"
echo "  Image     : $IMAGE_URI"
echo "=============================================="
echo ""
echo "Rollback command (if needed):"
echo "  kubectl rollout undo deployment/$APP_NAME -n $NAMESPACE"
