# FLUX structural proof worker.
# Uses the official ComfyUI Serverless base and the existing network volume.
# The volume must contain the FLUX base, VAE, text encoders, and XLabs ControlNet.
FROM runpod/worker-comfyui:5.8.4-base

# Standard ComfyUI nodes are sufficient for the first graph. The exact XLabs
# ControlNet format is validated by the ComfyUI loader at proof time.
COPY provision-assets.py /opt/dreamgen/provision-assets.py

# Download only at image build/startup into the persistent volume; never commit
# credentials or model bytes. The endpoint must be rebuilt from this file.
RUN python -m pip install --no-cache-dir huggingface_hub

RUN cat > /start-dreamgen-flux.sh <<'EOF'
#!/bin/bash
set -e
python /opt/dreamgen/provision-assets.py
exec /start.sh
EOF
RUN chmod +x /start-dreamgen-flux.sh

CMD ["/start-dreamgen-flux.sh"]
