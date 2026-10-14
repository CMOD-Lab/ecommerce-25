@echo off
setlocal enabledelayedexpansion

:: =============================================================================
:: deploy-image.bat - Deploy EcommerceWebApi to AWS EKS (Windows)
:: =============================================================================

set "APP_NAME=ecommercewebapi"
set "NAMESPACE=ecommercewebapi"
set "K8S_DIR=kubernetes"

echo ==============================================
echo   EcommerceWebApi - Deploy to AWS EKS
echo ==============================================
echo.

:: ---- Collect deployment inputs ----
set /p "AWS_REGION=Enter AWS Region (e.g. us-east-1): "
if "!AWS_REGION!"=="" (
    echo AWS Region is required. Exiting.
    exit /b 1
)

set /p "CLUSTER_NAME=Enter EKS Cluster Name: "
if "!CLUSTER_NAME!"=="" (
    echo EKS Cluster Name is required. Exiting.
    exit /b 1
)

set /p "IMAGE_URI=Enter full Docker image URI (e.g. 123456789.dkr.ecr.us-east-1.amazonaws.com/ecommercewebapi:latest): "
if "!IMAGE_URI!"=="" (
    echo Docker image URI is required. Exiting.
    exit /b 1
)

echo.
echo --- Application Environment Variables ---
echo Press Enter to skip any variable.
echo.

set /p "REDIS_CONNECTION_STRING=Enter REDIS_CONNECTION_STRING (e.g. my-redis.cache.amazonaws.com:6379): "
set /p "SIGNALR_CORS_ORIGINS=Enter SIGNALR_CORS_ORIGINS (comma-separated): "
set /p "APPLICATION_SECRET=Enter APPLICATION_SECRET (JWT signing secret): "

:: ---- Configure kubectl for EKS ----
echo.
echo Configuring kubectl for EKS cluster: !CLUSTER_NAME! in !AWS_REGION!...
aws eks update-kubeconfig --region !AWS_REGION! --name !CLUSTER_NAME!
if !ERRORLEVEL! neq 0 (
    echo Failed to configure kubectl. Exiting.
    exit /b 1
)

echo Verifying cluster connectivity...
kubectl cluster-info
if !ERRORLEVEL! neq 0 (
    echo Cannot connect to EKS cluster. Exiting.
    exit /b 1
)

:: ---- Copy manifests to temp directory ----
echo.
echo Updating Kubernetes manifests with deployment values...
set "DEPLOY_DIR=%TEMP%\k8s-deploy-%RANDOM%"
xcopy /E /I /Q "!K8S_DIR!" "!DEPLOY_DIR!" >nul

:: ---- Replace placeholders using PowerShell ----
powershell -NoProfile -Command ^
  "(Get-Content '!DEPLOY_DIR!\deployment.yaml') -replace '{{IMAGE_URI}}', '!IMAGE_URI!' | Set-Content '!DEPLOY_DIR!\deployment.yaml'"

if not "!REDIS_CONNECTION_STRING!"=="" (
    powershell -NoProfile -Command ^
      "(Get-Content '!DEPLOY_DIR!\deployment.yaml') -replace '{{REDIS_CONNECTION_STRING}}', '!REDIS_CONNECTION_STRING!' | Set-Content '!DEPLOY_DIR!\deployment.yaml'"
)

if not "!SIGNALR_CORS_ORIGINS!"=="" (
    powershell -NoProfile -Command ^
      "(Get-Content '!DEPLOY_DIR!\deployment.yaml') -replace '{{SIGNALR_CORS_ORIGINS}}', '!SIGNALR_CORS_ORIGINS!' | Set-Content '!DEPLOY_DIR!\deployment.yaml'"
)

if not "!APPLICATION_SECRET!"=="" (
    powershell -NoProfile -Command ^
      "(Get-Content '!DEPLOY_DIR!\deployment.yaml') -replace '{{APPLICATION_SECRET}}', '!APPLICATION_SECRET!' | Set-Content '!DEPLOY_DIR!\deployment.yaml'"
)

:: ---- Apply manifests in order ----
echo.
echo Applying Kubernetes manifests...

echo [1/4] Applying namespace...
kubectl apply -f "!DEPLOY_DIR!\namespace.yaml"
if !ERRORLEVEL! neq 0 ( echo Failed to apply namespace. & exit /b 1 )

echo [2/4] Applying deployment...
kubectl apply -f "!DEPLOY_DIR!\deployment.yaml"
if !ERRORLEVEL! neq 0 ( echo Failed to apply deployment. & exit /b 1 )

echo [3/4] Applying service...
kubectl apply -f "!DEPLOY_DIR!\service.yaml"
if !ERRORLEVEL! neq 0 ( echo Failed to apply service. & exit /b 1 )

echo [4/4] Applying ingress...
kubectl apply -f "!DEPLOY_DIR!\ingress.yaml"
if !ERRORLEVEL! neq 0 ( echo Failed to apply ingress. & exit /b 1 )

:: ---- Wait for rollout ----
echo.
echo Waiting for deployment rollout to complete...
kubectl rollout status deployment/!APP_NAME! -n !NAMESPACE! --timeout=300s
if !ERRORLEVEL! neq 0 (
    echo Deployment rollout did not complete successfully.
    echo Run: kubectl rollout undo deployment/!APP_NAME! -n !NAMESPACE!
    exit /b 1
)

:: ---- Verify resources ----
echo.
echo Verifying deployed resources...
kubectl get pods,svc,ingress -n !NAMESPACE!

:: ---- Cleanup temp files ----
rmdir /S /Q "!DEPLOY_DIR!" >nul 2>&1

echo.
echo ==============================================
echo   Deployment Complete!
echo   Namespace : !NAMESPACE!
echo   Image     : !IMAGE_URI!
echo ==============================================
echo.
echo Rollback command (if needed):
echo   kubectl rollout undo deployment/!APP_NAME! -n !NAMESPACE!

endlocal
exit /b 0
