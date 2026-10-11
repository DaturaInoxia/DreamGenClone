// The shared image inspector's pan/zoom engine.
//
// The card's full-size viewer renders the image at its NATURAL pixel size and then scale-transforms it, so 100% shows
// real pixels — a CSS-scaled <img> is exact, whereas sizing the bitmap down would inspect an interpolation instead of
// the render. Everything below is presentation only: it never re-encodes, resizes or uploads the bytes.
//
// All state lives on the viewport element, so a closed modal leaves nothing behind and two inspectors on one page
// cannot share a zoom level by accident.
window.imageZoom = {
    MIN_SCALE: 0.05,
    MAX_SCALE: 16,
    WHEEL_STEP: 1.15,
    BUTTON_STEP: 1.25,

    // Attaches the controls to one inspector viewport. Idempotent: Blazor re-renders the modal and calls this again.
    // The .NET reference lets the bare Escape key close the modal from here, where the key handling already lives.
    attach: function (viewport, dotNetRef) {
        if (!viewport || viewport.__zoomState) {
            return;
        }

        var image = viewport.querySelector('[data-zoom-image]');
        if (!image) {
            return;
        }

        var state = {
            scale: 1,
            fitScale: 1,
            x: 0,
            y: 0,
            dragging: false,
            pointerId: null,
            startX: 0,
            startY: 0,
            startOffsetX: 0,
            startOffsetY: 0,
            dotNetRef: dotNetRef || null
        };
        viewport.__zoomState = state;

        // The handlers are bound to the viewport, so a re-render of the image cannot strand them.
        state.onWheel = function (event) { window.imageZoom.handleWheel(viewport, event); };
        state.onPointerDown = function (event) { window.imageZoom.handlePointerDown(viewport, event); };
        state.onPointerMove = function (event) { window.imageZoom.handlePointerMove(viewport, event); };
        state.onPointerUp = function (event) { window.imageZoom.handlePointerUp(viewport, event); };
        state.onDoubleClick = function (event) { event.preventDefault(); window.imageZoom.toggleActualSize(viewport); };
        state.onKeyDown = function (event) { window.imageZoom.handleKeyDown(viewport, event); };

        viewport.addEventListener('wheel', state.onWheel, { passive: false });
        viewport.addEventListener('pointerdown', state.onPointerDown);
        viewport.addEventListener('pointermove', state.onPointerMove);
        viewport.addEventListener('pointerup', state.onPointerUp);
        viewport.addEventListener('pointercancel', state.onPointerUp);
        viewport.addEventListener('dblclick', state.onDoubleClick);

        // The buttons live INSIDE the viewport, so they are bound here rather than by the host page.
        state.onClick = function (event) {
            var button = event.target.closest('[data-zoom-action]');
            if (!button) {
                return;
            }

            event.preventDefault();
            var action = button.getAttribute('data-zoom-action');
            if (action === 'in') {
                window.imageZoom.zoomBy(viewport, window.imageZoom.BUTTON_STEP);
            } else if (action === 'out') {
                window.imageZoom.zoomBy(viewport, 1 / window.imageZoom.BUTTON_STEP);
            } else if (action === 'fit') {
                window.imageZoom.fit(viewport);
            } else if (action === 'actual') {
                window.imageZoom.actualSize(viewport);
            }
        };
        viewport.addEventListener('click', state.onClick);

        // Escape and the zoom keys are handled on the document because the modal itself is not focusable.
        state.onDocumentKeyDown = function (event) { window.imageZoom.handleKeyDown(viewport, event); };
        document.addEventListener('keydown', state.onDocumentKeyDown);

        // The natural size is only known once the bytes decode. A cached image is already complete, so both paths run.
        if (image.complete && image.naturalWidth > 0) {
            window.imageZoom.initialise(viewport);
        } else {
            image.addEventListener('load', function () { window.imageZoom.initialise(viewport); }, { once: true });
        }
    },

    // Sizes the bitmap to its real pixels and fits it to the viewport. Safe to call repeatedly.
    initialise: function (viewport) {
        var state = viewport.__zoomState;
        var image = viewport.querySelector('[data-zoom-image]');
        if (!state || !image || image.naturalWidth === 0) {
            return;
        }

        // Explicit pixel sizing: this is what makes scale 1 equal one image pixel per screen pixel.
        image.style.width = image.naturalWidth + 'px';
        image.style.height = image.naturalHeight + 'px';
        image.style.maxWidth = 'none';
        image.style.maxHeight = 'none';

        window.imageZoom.fit(viewport);
    },

    detach: function (viewport) {
        var state = viewport && viewport.__zoomState;
        if (!state) {
            return;
        }

        viewport.removeEventListener('wheel', state.onWheel);
        viewport.removeEventListener('pointerdown', state.onPointerDown);
        viewport.removeEventListener('pointermove', state.onPointerMove);
        viewport.removeEventListener('pointerup', state.onPointerUp);
        viewport.removeEventListener('pointercancel', state.onPointerUp);
        viewport.removeEventListener('dblclick', state.onDoubleClick);
        viewport.removeEventListener('click', state.onClick);
        document.removeEventListener('keydown', state.onDocumentKeyDown);
        delete viewport.__zoomState;
    },

    // Fit: the whole image inside the viewport, centred, never enlarged past 1:1.
    fit: function (viewport) {
        var state = viewport.__zoomState;
        var image = viewport.querySelector('[data-zoom-image]');
        if (!state || !image || image.naturalWidth === 0) {
            return;
        }

        var fitScale = Math.min(
            viewport.clientWidth / image.naturalWidth,
            viewport.clientHeight / image.naturalHeight);

        // A small image is not blown up to fill the box: seeing it smaller than the box is the honest view.
        state.fitScale = Math.min(fitScale, 1);
        state.scale = state.fitScale;
        state.x = (viewport.clientWidth - image.naturalWidth * state.scale) / 2;
        state.y = (viewport.clientHeight - image.naturalHeight * state.scale) / 2;
        window.imageZoom.apply(viewport);
    },

    // Actual size: one image pixel per screen pixel.
    actualSize: function (viewport) {
        var state = viewport.__zoomState;
        var image = viewport.querySelector('[data-zoom-image]');
        if (!state || !image || image.naturalWidth === 0) {
            return;
        }

        window.imageZoom.zoomTo(viewport, 1, viewport.clientWidth / 2, viewport.clientHeight / 2);
    },

    // Double-click toggles between the fitted view and real pixels.
    toggleActualSize: function (viewport) {
        var state = viewport.__zoomState;
        if (!state) {
            return;
        }

        if (Math.abs(state.scale - 1) < 0.001) {
            window.imageZoom.fit(viewport);
        } else {
            window.imageZoom.actualSize(viewport);
        }
    },

    zoomBy: function (viewport, factor) {
        window.imageZoom.zoomTo(viewport, window.imageZoom.scaleOf(viewport) * factor,
            viewport.clientWidth / 2, viewport.clientHeight / 2);
    },

    // Zooms to an absolute scale, holding the image point under (anchorX, anchorY) still — the behaviour that lets an
    // operator put the cursor on an eye and keep it there while magnifying.
    zoomTo: function (viewport, scale, anchorX, anchorY) {
        var state = viewport.__zoomState;
        var image = viewport.querySelector('[data-zoom-image]');
        if (!state || !image || image.naturalWidth === 0) {
            return;
        }

        var next = Math.min(Math.max(scale, window.imageZoom.MIN_SCALE), window.imageZoom.MAX_SCALE);
        var applied = state.scale || 1;

        // Viewport point -> image point, so the same rendezvous is re-solved at the new scale.
        var imageX = (anchorX - state.x) / applied;
        var imageY = (anchorY - state.y) / applied;

        state.scale = next;
        state.x = anchorX - imageX * next;
        state.y = anchorY - imageY * next;
        window.imageZoom.apply(viewport);
    },

    scaleOf: function (viewport) {
        var state = viewport.__zoomState;
        return state ? state.scale : 1;
    },

    handleWheel: function (viewport, event) {
        var state = viewport.__zoomState;
        if (!state) {
            return;
        }

        // The viewer owns the wheel while it is open, so the page behind it does not scroll mid-inspection.
        event.preventDefault();

        var rect = viewport.getBoundingClientRect();
        var anchorX = event.clientX - rect.left;
        var anchorY = event.clientY - rect.top;
        var factor = event.deltaY < 0 ? window.imageZoom.WHEEL_STEP : 1 / window.imageZoom.WHEEL_STEP;
        window.imageZoom.zoomTo(viewport, state.scale * factor, anchorX, anchorY);
    },

    handlePointerDown: function (viewport, event) {
        var state = viewport.__zoomState;
        if (!state || event.button !== 0) {
            return;
        }

        // Buttons keep their own click behaviour; dragging must not start on them.
        if (event.target.closest('[data-zoom-action]')) {
            return;
        }

        state.dragging = true;
        state.pointerId = event.pointerId;
        state.startX = event.clientX;
        state.startY = event.clientY;
        state.startOffsetX = state.x;
        state.startOffsetY = state.y;
        viewport.setPointerCapture(event.pointerId);
        viewport.classList.add('is-panning');
    },

    handlePointerMove: function (viewport, event) {
        var state = viewport.__zoomState;
        if (!state || !state.dragging || event.pointerId !== state.pointerId) {
            return;
        }

        state.x = state.startOffsetX + (event.clientX - state.startX);
        state.y = state.startOffsetY + (event.clientY - state.startY);
        window.imageZoom.apply(viewport);
    },

    handlePointerUp: function (viewport, event) {
        var state = viewport.__zoomState;
        if (!state || !state.dragging || event.pointerId !== state.pointerId) {
            return;
        }

        state.dragging = false;
        state.pointerId = null;
        if (viewport.hasPointerCapture(event.pointerId)) {
            viewport.releasePointerCapture(event.pointerId);
        }

        viewport.classList.remove('is-panning');
    },

    handleKeyDown: function (viewport, event) {
        var state = viewport.__zoomState;

        // Only while this inspector is on screen: a closed modal's listener is detached, but a second open one would
        // otherwise react to the first one's keys.
        if (!state || !viewport.isConnected) {
            return;
        }

        var target = event.target;
        if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) {
            return;
        }

        // Ctrl/Cmd/Alt combinations belong to the browser (page zoom, dev tools, menu access): the inspector only
        // claims the bare keys, so inspecting an image never blocks zooming the app itself.
        if (event.ctrlKey || event.metaKey || event.altKey) {
            return;
        }

        if (event.key === '0') {
            event.preventDefault();
            window.imageZoom.fit(viewport);
        } else if (event.key === 'Escape') {
            // Closing is the host's decision, so the request goes back to .NET rather than tearing down the DOM here.
            event.preventDefault();
            if (state.dotNetRef) {
                state.dotNetRef.invokeMethodAsync('OnEscapeRequested');
            }
        } else if (event.key === '+' || event.key === '=') {
            event.preventDefault();
            window.imageZoom.zoomBy(viewport, window.imageZoom.BUTTON_STEP);
        } else if (event.key === '-' || event.key === '_') {
            event.preventDefault();
            window.imageZoom.zoomBy(viewport, 1 / window.imageZoom.BUTTON_STEP);
        } else if (event.key === '1') {
            event.preventDefault();
            window.imageZoom.actualSize(viewport);
        }
    },

    // Writes the transform and the readout. translate-then-scale with a 0 0 origin keeps the zoom-anchor math linear.
    apply: function (viewport) {
        var state = viewport.__zoomState;
        var image = viewport.querySelector('[data-zoom-image]');
        if (!state || !image) {
            return;
        }

        image.style.transformOrigin = '0 0';
        image.style.transform = 'translate(' + state.x + 'px, ' + state.y + 'px) scale(' + state.scale + ')';

        var readout = viewport.querySelector('[data-zoom-readout]');
        if (readout) {
            readout.textContent = Math.round(state.scale * 100) + '%';
        }

        var actualButton = viewport.querySelector('[data-zoom-action="actual"]');
        if (actualButton) {
            var atActual = Math.abs(state.scale - 1) < 0.001;
            actualButton.classList.toggle('active', atActual);
        }
    }
};
