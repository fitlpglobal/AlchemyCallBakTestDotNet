#!/bin/sh
cd AlchemyCallbackTest
dotnet run --urls=http://0.0.0.0:${PORT:-8080}
