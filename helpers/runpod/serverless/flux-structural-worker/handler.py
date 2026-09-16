"""GitHub integration validation handler.

The runtime is supplied by the official runpod/worker-comfyui base image; this
file exists so RunPod's GitHub import validator can detect the required
runpod.serverless.start() contract. The Dockerfile does not copy or execute
this file because the base image owns the ComfyUI workflow handler.
"""

import runpod


def handler(job):
    raise RuntimeError(
        "The FLUX proof must run through the official worker-comfyui base handler; "
        "this repository validation handler is not the runtime entrypoint."
    )


runpod.serverless.start({"handler": handler})
