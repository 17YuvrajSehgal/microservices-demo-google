# Minimal setup.py so consumers can `pip install -e ../_shared-python/rpc_logging`.
# M2.1 (microservice-changes-todo.md).

from setuptools import setup, find_packages

setup(
    name="hipstershop-rpc-logging",
    version="0.0.1",
    description="Shared per-RPC structured logging interceptor for Python gRPC services.",
    packages=["rpc_logging"],
    package_dir={"rpc_logging": "."},
    install_requires=[
        "grpcio>=1.60",
        "opentelemetry-api>=1.20",
    ],
)
