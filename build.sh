#!/bin/bash
echo "Building C# Document Validator..."
dotnet --version
dotnet restore
dotnet build
echo "Build completed." 