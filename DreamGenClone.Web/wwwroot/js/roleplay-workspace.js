window.rolePlayWorkspace = {
    // Scroll the story container to the bottom after new interactions load.
    scrollStoryToBottom: function () {
        const el = document.querySelector('.rw-story');
        if (el) { el.scrollTop = el.scrollHeight; }
    },

    // Return the bounding rect of the element matching the selector.
    getElementRect: function (selector) {
        const el = document.querySelector(selector);
        if (!el) return null;
        const r = el.getBoundingClientRect();
        return { top: r.top, left: r.left, bottom: r.bottom, right: r.right, width: r.width, height: r.height };
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
    },

    disposePanelResize: function (handleSelector) {
        const handle = document.querySelector(handleSelector);
        if (handle && typeof handle.__rwResizeDispose === 'function') {
            handle.__rwResizeDispose();
            handle.__rwResizeDispose = null;
        }
    },

    openDebugWindow: function (url) {
        if (!url) {
            return;
        }

        const features = 'popup=yes,width=1600,height=960,resizable=yes,scrollbars=yes';
        window.open(url, 'RolePlayDebugWindow', features);
    }
};
