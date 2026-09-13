window.rolePlayWorkspace = {
    // Scroll the story container to the bottom after new interactions load.
    scrollStoryToBottom: function () {
        const el = document.querySelector('.rw-story');
        if (el) { el.scrollTop = el.scrollHeight; }
    },

    // Check if story view is near bottom before applying auto-follow.
    isStoryNearBottom: function (thresholdPx) {
        const el = document.querySelector('.rw-story');
        if (!el) {
            return true;
        }

        const threshold = typeof thresholdPx === 'number' ? thresholdPx : 80;
        const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
        return distance <= threshold;
    },

    // Follow new story chunks only when the user is already near the bottom.
    followStoryIfNearBottom: function (thresholdPx) {
        const el = document.querySelector('.rw-story');
        if (!el) {
            return true;
        }

        const threshold = typeof thresholdPx === 'number' ? thresholdPx : 80;
        const distance = el.scrollHeight - el.scrollTop - el.clientHeight;
        const nearBottom = distance <= threshold;
        if (nearBottom) {
            el.scrollTop = el.scrollHeight;
        }

        return nearBottom;
    },

    scrollElementToBottom: function (element) {
        if (!element) {
            return;
        }

        element.scrollTop = element.scrollHeight;
    },

    copyTextToClipboard: async function (text) {
        if (!navigator.clipboard || typeof navigator.clipboard.writeText !== 'function') {
            return false;
        }

        try {
            await navigator.clipboard.writeText(text ?? '');
            return true;
        } catch {
            return false;
        }
    },

    // Return the bounding rect of the element matching the selector.
    getElementRect: function (selector) {
        const el = document.querySelector(selector);
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return { top: r.top, left: r.left, bottom: r.bottom, right: r.right, width: r.width, height: r.height };
    },

    // Crop box dragging. The script only reports the pointer (stage-normalized) and which corner handle
    // was grabbed; the component decides the window, so the arithmetic stays in one testable place.
    initCropDrag: function (stageSelector, dotNetRef) {
        const stage = document.querySelector(stageSelector);
        if (!stage) {
            return;
        }

        // Idempotent: the component re-asserts this after renders, and an element that is already
        // listening must not gain a second listener.
        if (typeof stage.__cropDragDispose === 'function') {
            return;
        }

        let active = false;
        let grabbedHandle = '';

        const report = function (phase, event) {
            const rect = stage.getBoundingClientRect();
            if (rect.width <= 0 || rect.height <= 0) {
                return;
            }

            const normalizedX = (event.clientX - rect.left) / rect.width;
            const normalizedY = (event.clientY - rect.top) / rect.height;
            dotNetRef.invokeMethodAsync('OnCropDragAsync', phase, grabbedHandle, normalizedX, normalizedY);
        };

        const onPointerMove = function (event) {
            if (!active) return;
            if (event.cancelable) event.preventDefault();
            report('move', event);
        };

        const onPointerUp = function (event) {
            if (!active) return;
            report('end', event);
            active = false;
            grabbedHandle = '';
            stage.classList.remove('is-crop-dragging');
            document.removeEventListener('pointermove', onPointerMove);
            document.removeEventListener('pointerup', onPointerUp);
            document.removeEventListener('mousemove', onPointerMove);
            document.removeEventListener('mouseup', onPointerUp);
        };

        const onDownEvent = function (event) {
            if (active) return;
            if (event.type === 'mousedown' && event.button !== 0) return;
            if (event.type === 'pointerdown' && event.button !== 0) return;
            // The handle is taken from the element the drag STARTED on: later move/up events target the
            // document (or whatever is under the cursor), which would look like a body drag.
            grabbedHandle = (event.target && event.target.dataset && event.target.dataset.cropHandle) || '';
            active = true;
            stage.classList.add('is-crop-dragging');
            report('start', event);
            document.addEventListener('pointermove', onPointerMove);
            document.addEventListener('pointerup', onPointerUp);
            document.addEventListener('mousemove', onPointerMove);
            document.addEventListener('mouseup', onPointerUp);
        };

        // Pointer events are the primary path; mouse events cover clients that do not emit them for mouse
        // input. Both are bound and guarded by `active`, so one drag never starts twice.
        stage.addEventListener('pointerdown', onDownEvent);
        stage.addEventListener('mousedown', onDownEvent);
        stage.__cropDragDispose = function () {
            stage.removeEventListener('pointerdown', onDownEvent);
            stage.removeEventListener('mousedown', onDownEvent);
            document.removeEventListener('pointermove', onPointerMove);
            document.removeEventListener('pointerup', onPointerUp);
            document.removeEventListener('mousemove', onPointerMove);
            document.removeEventListener('mouseup', onPointerUp);
            stage.classList.remove('is-crop-dragging');
        };
    },

    disposeCropDrag: function (stageSelector) {
        const stage = document.querySelector(stageSelector);
        if (stage && typeof stage.__cropDragDispose === 'function') {
            stage.__cropDragDispose();
            stage.__cropDragDispose = null;
        }
    },

    initPanelResize: function (shellSelector, handleSelector, initialWidth, minWidth, maxWidth, dotNetRef) {
        const shell = document.querySelector(shellSelector);
        const handle = document.querySelector(handleSelector);
        if (!shell || !handle) {
            return;
        }

        let startX = 0;
        let startWidth = initialWidth;
        let activeDotNetRef = dotNetRef;

        const applyWidth = function (value) {
            const width = Math.max(minWidth, Math.min(maxWidth, Math.round(value)));
            shell.style.setProperty('--rw-settings-width', width + 'px');
            return width;
        };

        applyWidth(initialWidth);

        const onPointerMove = function (event) {
            const delta = startX - event.clientX;
            const next = applyWidth(startWidth + delta);
            if (activeDotNetRef && typeof activeDotNetRef.invokeMethodAsync === 'function') {
                activeDotNetRef.invokeMethodAsync('OnSettingsPanelResized', next);
            }
        };

        const onPointerUp = function () {
            document.removeEventListener('pointermove', onPointerMove);
            document.removeEventListener('pointerup', onPointerUp);
            shell.classList.remove('is-resizing');
        };

        const onPointerDown = function (event) {
            if (shell.classList.contains('panel-collapsed')) return;
            startX = event.clientX;
            const styleWidth = parseInt(getComputedStyle(shell).getPropertyValue('--rw-settings-width'), 10);
            startWidth = Number.isNaN(styleWidth) ? initialWidth : styleWidth;
            shell.classList.add('is-resizing');
            document.addEventListener('pointermove', onPointerMove);
            document.addEventListener('pointerup', onPointerUp);
        };

        handle.addEventListener('pointerdown', onPointerDown);
        handle.__rwResizeDispose = function () {
            activeDotNetRef = null;
            handle.removeEventListener('pointerdown', onPointerDown);
            document.removeEventListener('pointermove', onPointerMove);
            document.removeEventListener('pointerup', onPointerUp);
            shell.classList.remove('is-resizing');
        };

        // Restore collapse state from previous session
        if (localStorage.getItem('rw-panel-collapsed') === '1') {
            shell.classList.add('panel-collapsed');
        }
    },

    disposePanelResize: function (handleSelector) {
        const handle = document.querySelector(handleSelector);
        if (handle && typeof handle.__rwResizeDispose === 'function') {
            handle.__rwResizeDispose();
            handle.__rwResizeDispose = null;
        }
    },

    toggleSettingsPanel: function (event) {
        event.stopPropagation();
        const shell = document.querySelector('.rw-shell');
        if (!shell) return;
        const collapsed = shell.classList.toggle('panel-collapsed');
        localStorage.setItem('rw-panel-collapsed', collapsed ? '1' : '0');
    },

    openDebugWindow: function (url) {
        if (!url) {
            return;
        }

        const features = 'popup=yes,width=1600,height=960,resizable=yes,scrollbars=yes';
        window.open(url, 'RolePlayDebugWindow', features);
    },

    // ── Read Cursor ──────────────────────────────────────────────────────────

    _readCursorObserver: null,
    _readCursorDotNetRef: null,

    scrollToCursor: function () {
        const el = document.getElementById('rw-read-cursor');
        if (el) { el.scrollIntoView({ behavior: 'instant', block: 'start' }); }
    },

    initReadCursorObserver: function (dotNetRef, totalCount, cursorIndex) {
        // Tear down previous observer.
        if (this._readCursorObserver) {
            this._readCursorObserver.disconnect();
            this._readCursorObserver = null;
        }
        this._readCursorDotNetRef = dotNetRef;

        const story = document.querySelector('.rw-story');
        if (!story || totalCount === 0) { return; }

        // Start the high-watermark at the current C# cursor position so we never
        // call back with an index the Blazor side has already passed.
        const highWaterRef = { value: typeof cursorIndex === 'number' ? cursorIndex : -1 };

        const observer = new IntersectionObserver(function (entries) {
            let advanced = false;
            entries.forEach(function (entry) {
                if (!entry.isIntersecting) {
                    // Only advance when the element has scrolled upward past the container top.
                    const storyRect = story.getBoundingClientRect();
                    const entryRect = entry.boundingClientRect;
                    if (entryRect.bottom < storyRect.top) {
                        const idx = parseInt(entry.target.dataset.interactionIndex, 10);
                        if (!isNaN(idx) && idx > highWaterRef.value) {
                            highWaterRef.value = idx;
                            advanced = true;
                        }
                    }
                }
            });
            if (advanced && dotNetRef && typeof dotNetRef.invokeMethodAsync === 'function') {
                dotNetRef.invokeMethodAsync('AdvanceReadCursor', highWaterRef.value);
            }
        }, {
            root: story,
            threshold: 0
        });

        document.querySelectorAll('.rw-interaction[data-interaction-index]').forEach(function (el) {
            observer.observe(el);
        });

        this._readCursorObserver = observer;
    },

    disposeReadCursorObserver: function () {
        if (this._readCursorObserver) {
            this._readCursorObserver.disconnect();
            this._readCursorObserver = null;
        }
        this._readCursorDotNetRef = null;
    }
};
