@echo off
setlocal enabledelayedexpansion

:: =============================================================================
:: build-push.bat - Build and push EcommerceWebApi Docker image (Windows)
:: =============================================================================

set "PROJECT_NAME=ecommercewebapi"
set "DOCKERFILE_PATH=Dockerfile"

echo ==============================================
echo   EcommerceWebApi - Docker Build ^& Push
echo ==============================================
echo.

:: Sanitize image name using PowerShell
for /f "delims=" %%i in ('powershell -NoProfile -Command "$n = 'ecommercewebapi'; $n = $n.ToLower() -replace '[^a-z0-9]','-'; $n = $n.Trim('-'); Write-Output $n"') do set "IMAGE_NAME=%%i"

:: Prompt for image tag
set /p "IMAGE_TAG_INPUT=Enter image tag [latest]: "
if "!IMAGE_TAG_INPUT!"=="" set "IMAGE_TAG_INPUT=latest"
for /f "delims=" %%i in ('powershell -NoProfile -Command "$t = '!IMAGE_TAG_INPUT!'; $t = $t.ToLower() -replace '[^a-z0-9._-]','-'; $t = $t.Trim('-'); if ($t -eq '') { $t = 'latest' }; Write-Output $t"') do set "IMAGE_TAG=%%i"
echo Using image tag: !IMAGE_TAG!
echo.

:: Registry selection
echo Select container registry:
echo   1. AWS ECR
echo   2. Docker Hub
set /p "REGISTRY_CHOICE=Enter choice [1]: "
if "!REGISTRY_CHOICE!"=="" set "REGISTRY_CHOICE=1"

if "!REGISTRY_CHOICE!"=="1" goto :ecr
if "!REGISTRY_CHOICE!"=="2" goto :dockerhub
echo Invalid registry choice. Exiting.
exit /b 1

:ecr
echo.
echo --- AWS ECR Configuration ---
set /p "AWS_REGION=Enter AWS Region (e.g. us-east-1): "
set /p "AWS_ACCOUNT_ID=Enter AWS Account ID: "
set /p "ECR_REPO_INPUT=Enter ECR Repository name [!IMAGE_NAME!]: "
if "!ECR_REPO_INPUT!"=="" set "ECR_REPO_INPUT=!IMAGE_NAME!"
set "ECR_REPO=!ECR_REPO_INPUT!"

set "REGISTRY_URL=!AWS_ACCOUNT_ID!.dkr.ecr.!AWS_REGION!.amazonaws.com"
set "FULL_IMAGE_NAME=!REGISTRY_URL!/!ECR_REPO!:!IMAGE_TAG!"

echo.
echo Logging in to AWS ECR...
aws ecr get-login-password --region !AWS_REGION! | docker login --username AWS --password-stdin !REGISTRY_URL!
if !ERRORLEVEL! neq 0 (
    echo ECR login failed.
    exit /b 1
)

echo Ensuring ECR repository exists...
aws ecr describe-repositories --repository-names !ECR_REPO! --region !AWS_REGION! >nul 2>&1
if !ERRORLEVEL! neq 0 (
    echo Creating ECR repository...
    aws ecr create-repository --repository-name !ECR_REPO! --region !AWS_REGION!
    if !ERRORLEVEL! neq 0 (
        echo Failed to create ECR repository.
        exit /b 1
    )
)
goto :build

:dockerhub
echo.
echo --- Docker Hub Configuration ---
set /p "DOCKER_USERNAME=Enter Docker Hub username: "
set /p "DOCKER_PASSWORD=Enter Docker Hub password/token: "
set /p "DOCKER_REPO_INPUT=Enter Docker Hub repository [!DOCKER_USERNAME!/!IMAGE_NAME!]: "
if "!DOCKER_REPO_INPUT!"=="" set "DOCKER_REPO_INPUT=!DOCKER_USERNAME!/!IMAGE_NAME!"
set "DOCKER_REPO=!DOCKER_REPO_INPUT!"

set "FULL_IMAGE_NAME=!DOCKER_REPO!:!IMAGE_TAG!"

echo.
echo Logging in to Docker Hub...
echo !DOCKER_PASSWORD! | docker login --username !DOCKER_USERNAME! --password-stdin
if !ERRORLEVEL! neq 0 (
    echo Docker Hub login failed.
    exit /b 1
)
goto :build

:build
echo.
echo Building Docker image: !FULL_IMAGE_NAME!
echo Build context: . (repository root)
docker build -f "!DOCKERFILE_PATH!" -t "!FULL_IMAGE_NAME!" .
if !ERRORLEVEL! neq 0 (
    echo Docker build failed.
    exit /b 1
)

echo.
echo Pushing image: !FULL_IMAGE_NAME!
docker push "!FULL_IMAGE_NAME!"
if !ERRORLEVEL! neq 0 (
    echo Docker push failed.
    exit /b 1
)

echo.
echo ==============================================
echo   Build ^& Push Complete!
echo   Image: !FULL_IMAGE_NAME!
echo ==============================================

endlocal
exit /b 0
