// Browser-only state preview for explicitly authored URUI controls.
// The preview tool loads this helper; production HTML does not require a script element.
(() => {
    const bindings = new WeakMap();
    const selectBindings = new WeakMap();
    const scrollViews = new WeakSet();
    const scrollBindings = new Map();
    const foldouts = new WeakMap();
    const progressBars = new WeakMap();
    function refreshSlider(host) {
        const input = host.querySelector(':scope > input[type=range]');
        const fill = host.querySelector(':scope > .urui-slider-fill');
        const thumb = host.querySelector(':scope > .urui-slider-thumb');
        if (!input || !fill || !thumb) return;
        let binding = bindings.get(input);
        if (!binding) {
            binding = { direction: null, pointer: null, start: null };
            bindings.set(input, binding);
            bindSlider(host, input, binding);
        }
        const direction = input.getAttribute('data-urui-orientation') === 'vertical' ? 'vertical' : 'horizontal';
        if (binding.direction !== null && binding.direction !== direction) {
            fill.style.removeProperty(binding.direction === 'vertical' ? 'height' : 'width');
            thumb.style.removeProperty(binding.direction === 'vertical' ? 'top' : 'left');
        }
        binding.direction = direction;
        const [low, high] = rangeBounds(input, binding);
        const percent = high > low ? Math.max(0, Math.min(100, (input.valueAsNumber - low) / (high - low) * 100)) : 0;
        fill.style.setProperty(direction === 'vertical' ? 'height' : 'width', percent + '%');
        thumb.style.setProperty(direction === 'vertical' ? 'top' : 'left', percent + '%');
        if (host.getAttribute('data-urui-orientation') !== direction)
            host.setAttribute('data-urui-orientation', direction);
    }
    function rangeBounds(input, binding) {
        // Use the browser's range rules, including invalid attributes and max < min.
        // parseFloat would incorrectly accept a numeric prefix such as "10invalid".
        const probe = binding.bounds ??= input.ownerDocument.createElement('input');
        probe.type = 'range'; probe.step = 'any'; probe.min = input.min; probe.max = input.max;
        probe.valueAsNumber = -Number.MAX_VALUE;
        const low = probe.valueAsNumber;
        probe.valueAsNumber = Number.MAX_VALUE;
        return [low, probe.valueAsNumber];
    }
    function bindSlider(host, input, binding) {
        const emit = name => input.dispatchEvent(new Event(name, { bubbles: true }));
        const update = event => {
            const rect = input.getBoundingClientRect();
            const vertical = binding.direction === 'vertical';
            const length = vertical ? rect.height : rect.width;
            if (length <= 0) return;
            const ratio = Math.max(0, Math.min(1, ((vertical ? event.clientY : event.clientX) - (vertical ? rect.y : rect.x)) / length));
            const [low, high] = rangeBounds(input, binding);
            const previous = input.value;
            input.valueAsNumber = low + (high - low) * ratio;
            refreshSlider(host);
            if (input.value !== previous) emit('input');
        };
        const finish = commit => {
            if (binding.pointer === null) return;
            const changed = input.value !== binding.start;
            binding.pointer = null;
            if (commit && changed) emit('change');
        };
        input.addEventListener('input', () => refreshSlider(host));
        input.addEventListener('change', () => refreshSlider(host));
        input.addEventListener('pointerdown', event => {
            if (input.matches(':disabled') || event.button !== 0 || binding.pointer !== null) return;
            event.preventDefault();
            binding.pointer = event.pointerId;
            binding.start = input.value;
            input.focus();
            input.setPointerCapture(event.pointerId);
            update(event);
        });
        input.addEventListener('pointermove', event => {
            if (event.pointerId === binding.pointer && !input.matches(':disabled')) update(event);
        });
        input.addEventListener('pointerup', event => {
            if (event.pointerId !== binding.pointer) return;
            if (!input.matches(':disabled')) update(event);
            finish(!input.matches(':disabled'));
            input.releasePointerCapture(event.pointerId);
        });
        input.addEventListener('pointercancel', () => finish(false));
        input.addEventListener('lostpointercapture', () => finish(false));
        input.addEventListener('blur', () => finish(true));
        input.addEventListener('keydown', event => {
            if (binding.direction !== 'vertical' || !['ArrowUp', 'ArrowDown'].includes(event.key) || input.matches(':disabled')) return;
            event.preventDefault();
            const previous = input.value;
            // stepUp/stepDown use the browser's native range sanitization and step base.
            if (input.step === 'any') input.valueAsNumber += event.key === 'ArrowDown' ? 1 : -1;
            else if (event.key === 'ArrowDown') input.stepUp();
            else input.stepDown();
            refreshSlider(host);
            if (input.value !== previous) { emit('input'); emit('change'); }
        });
    }
    function refreshSelect(host) {
        const input = host.querySelector(':scope > select');
        const trigger = host.querySelector(':scope > .urui-select-trigger');
        const label = trigger?.querySelector('.urui-select-value');
        const popup = host.querySelector(':scope > .urui-select-options');
        const prototype = host.querySelector(':scope > .urui-select-option[hidden]');
        if (!input || !trigger || !label || !popup || !prototype) return;
        let binding = selectBindings.get(input);
        if (!binding) {
            binding = { open: false, initialized: false, generation: 0, animation: null, signature: null, rows: [] };
            for (const row of popup.querySelectorAll(':scope > [data-urui-option-index]')) row.remove();
            selectBindings.set(input, binding);
            const setOpen = value => {
                const next = value && !input.matches(':disabled');
                if (binding.initialized && next === binding.open) return;
                const previous = binding.open;
                const generation = ++binding.generation;
                binding.initialized = true;
                binding.animation?.cancel(); binding.animation = null;
                popup.classList.remove('urui-select-opening', 'urui-select-closing');
                binding.open = next;
                popup.hidden = !next;
                trigger.setAttribute('aria-expanded', String(next));
                if (!(next || previous) || Number(getComputedStyle(popup).getPropertyValue('--urui-select-fade')) <= 0) return;
                // 重开先撤销旧动画；仅本次身份的完成回调可以清理当前状态。
                void popup.offsetWidth;
                const phase = next ? 'urui-select-opening' : 'urui-select-closing';
                popup.classList.add(phase);
                const name = next ? 'urui-select-open' : 'urui-select-close';
                const animation = popup.getAnimations().find(value => value.animationName === name);
                if (!animation) { popup.classList.remove(phase); return; }
                binding.animation = animation;
                animation.finished.then(() => {
                    if (binding.generation !== generation) return;
                    binding.animation = null; popup.classList.remove(phase);
                }, error => { if (error.name !== 'AbortError') throw error; });
            };
            const select = (index, close) => {
                const option = input.options[index];
                if (input.matches(':disabled') || !option || option.disabled || option.hidden) return;
                const previous = input.value;
                input.selectedIndex = index;
                refreshSelect(host);
                if (input.value !== previous) {
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    input.dispatchEvent(new Event('change', { bubbles: true }));
                }
                if (close) { setOpen(false); trigger.focus(); }
            };
            binding.select = select;
            binding.setOpen = setOpen;
            trigger.addEventListener('click', () => setOpen(!binding.open));
            input.addEventListener('input', () => refreshSelect(host));
            input.addEventListener('change', () => refreshSelect(host));
            host.addEventListener('keydown', event => {
                if (input.matches(':disabled')) return;
                if (event.key === 'Escape' && binding.open) {
                    event.preventDefault(); event.stopPropagation(); setOpen(false); trigger.focus(); return;
                }
                const direction = event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0;
                const first = event.key === 'Home', last = event.key === 'End';
                if (!direction && !first && !last) return;
                event.preventDefault(); event.stopPropagation();
                let index = first ? 0 : last ? input.options.length - 1 : input.selectedIndex + direction;
                const step = last ? -1 : direction || 1;
                while (index >= 0 && index < input.options.length && (input.options[index].disabled || input.options[index].hidden)) index += step;
                select(index, false);
            });
            const outside = event => { if (binding.open && !host.contains(event.target)) setOpen(false); };
            // Observe outside interactions once per preview document, not once per control.
            let owners = previewDocuments.get(host.ownerDocument);
            if (!owners) {
                owners = new Set(); previewDocuments.set(host.ownerDocument, owners);
                const notify = event => {
                    for (const owner of owners) {
                        if (!owner.host.isConnected) owners.delete(owner);
                        else owner.outside(event);
                    }
                };
                host.ownerDocument.addEventListener('pointerdown', notify, true);
                host.ownerDocument.addEventListener('focusin', notify, true);
            }
            owners.add({ host, outside });
            setOpen(false);
        }
        const options = [...input.options];
        const signature = JSON.stringify(options.map(option => [option.value, option.label, option.disabled, option.hidden]));
        if (signature !== binding.signature) {
            for (const row of binding.rows) row.remove();
            binding.rows = options.map((option, index) => {
                const row = prototype.cloneNode(true);
                row.removeAttribute('id'); row.hidden = option.hidden; row.textContent = option.label; row.disabled = option.disabled;
                row.setAttribute('data-urui-option-index', String(index));
                row.addEventListener('click', () => binding.select(index, true));
                popup.append(row); return row;
            });
            binding.signature = signature;
        }
        const text = options[input.selectedIndex]?.label || '';
        if (label.textContent !== text) label.textContent = text;
        const disabled = input.matches(':disabled');
        if (trigger.disabled !== disabled) trigger.disabled = disabled;
        binding.rows.forEach((row, index) => {
            row.classList.toggle('urui-selected', index === input.selectedIndex);
            row.setAttribute('aria-selected', String(index === input.selectedIndex));
        });
        if (disabled) binding.setOpen(false);
    }
    const previewDocuments = new WeakMap();
    function refreshScrollView(viewport) {
        if (scrollViews.has(viewport)) return;
        const contents = [...viewport.children].filter(child => child.classList.contains('urui-scroll-content'));
        const outside = [...viewport.childNodes].some(child => child.nodeType === Node.ELEMENT_NODE ? !contents.includes(child)
            : child.nodeType === Node.TEXT_NODE && /[^\t\n\f\r ]/.test(child.data));
        if (contents.length > 1 || outside) throw new Error('urui-scroll-view requires one explicit .urui-scroll-content for author content.');
        if (!contents.length) {
            const content = viewport.ownerDocument.createElement('div');
            content.className = 'urui-scroll-content';
            Object.assign(content.style, { display: 'block', position: 'relative', width: '100%' });
            Object.assign(viewport.style, { display: 'block', overflow: 'auto' });
            viewport.append(content);
        }
        if (!viewport.hasAttribute('tabindex')) viewport.tabIndex = 0;
        viewport.addEventListener('keydown', event => {
            if (event.target !== viewport || viewport.closest('[disabled]')) return;
            if (event.key === 'Home') { viewport.scrollLeft = 0; viewport.scrollTop = 0; }
            else if (event.key === 'End') viewport.scrollTop = viewport.scrollHeight;
            else if (event.key === 'PageUp') viewport.scrollTop -= viewport.clientHeight;
            else if (event.key === 'PageDown') viewport.scrollTop += viewport.clientHeight;
            else if (event.key === 'ArrowUp') viewport.scrollTop -= 40;
            else if (event.key === 'ArrowDown') viewport.scrollTop += 40;
            else if (event.key === 'ArrowLeft') viewport.scrollLeft -= 40;
            else if (event.key === 'ArrowRight') viewport.scrollLeft += 40;
            else return;
            event.preventDefault();
        });
        scrollViews.add(viewport);
    }
    function refreshScrollbar(track) {
        const viewport = track.ownerDocument.getElementById(track.getAttribute('aria-controls'));
        const thumb = track.querySelector(':scope > .urui-scrollbar-thumb');
        if (!viewport || !thumb) return;
        let binding = scrollBindings.get(track);
        if (!binding) {
            binding = { viewport, axis: null, pointer: null, anchor: 0, length: 0, position: 0, trackLength: 0, limit: 0, page: 0 };
            const disabled = () => !!viewport.closest('[disabled]');
            const axis = () => track.getAttribute('data-urui-orientation') === 'horizontal' ? 0 : 1;
            const offset = () => binding.axis === 0 ? viewport.scrollLeft : viewport.scrollTop;
            const scroll = value => {
                if (disabled()) return;
                if (binding.axis === 0) viewport.scrollLeft = value; else viewport.scrollTop = value;
                update();
            };
            const coordinate = event => {
                const rect = track.getBoundingClientRect();
                return binding.axis === 0 ? event.clientX - rect.x : event.clientY - rect.y;
            };
            const finish = () => {
                const pointer = binding.pointer; binding.pointer = null;
                if (pointer !== null && track.hasPointerCapture(pointer)) track.releasePointerCapture(pointer);
            };
            const update = () => {
                const direction = axis();
                if (binding.axis !== null && binding.axis !== direction) {
                    finish();
                    thumb.style.removeProperty(binding.axis === 0 ? 'width' : 'height');
                    thumb.style.removeProperty(binding.axis === 0 ? 'left' : 'top');
                }
                binding.axis = direction;
                binding.page = direction === 0 ? viewport.clientWidth : viewport.clientHeight;
                const content = Math.max(binding.page, direction === 0 ? viewport.scrollWidth : viewport.scrollHeight);
                binding.trackLength = direction === 0 ? track.clientWidth : track.clientHeight;
                binding.limit = Math.max(0, content - binding.page);
                const value = getComputedStyle(thumb)[direction === 0 ? 'minWidth' : 'minHeight'];
                const minimum = value.endsWith('%') ? parseFloat(value) * binding.trackLength / 100 : parseFloat(value) || 0;
                binding.length = Math.min(binding.trackLength, Math.max(0, minimum, content > 0 ? binding.trackLength * binding.page / content : binding.trackLength));
                binding.position = binding.limit > 0 ? (binding.trackLength - binding.length) * offset() / binding.limit : 0;
                const size = direction === 0 ? 'width' : 'height', position = direction === 0 ? 'left' : 'top';
                const sizeText = binding.length + 'px', positionText = binding.position + 'px';
                if (thumb.style[size] !== sizeText) thumb.style[size] = sizeText;
                if (thumb.style[position] !== positionText) thumb.style[position] = positionText;
                track.style.visibility = binding.limit > 0 && binding.page > 0 && binding.trackLength > 0 ? 'visible' : 'hidden';
                track.setAttribute('aria-valuemin', '0'); track.setAttribute('aria-valuemax', String(binding.limit));
                track.setAttribute('aria-valuenow', String(offset())); track.setAttribute('aria-orientation', direction === 0 ? 'horizontal' : 'vertical');
                track.setAttribute('aria-disabled', String(disabled()));
            };
            const drag = event => {
                const travel = binding.trackLength - binding.length;
                scroll(travel > 0 ? Math.max(0, Math.min(1, (coordinate(event) - binding.anchor) / travel)) * binding.limit : 0);
            };
            track.addEventListener('pointerdown', event => {
                if (disabled() || event.button !== 0 || binding.pointer !== null || binding.limit <= 0) return;
                event.preventDefault(); event.stopPropagation(); track.focus();
                const point = coordinate(event);
                if (point < binding.position || point > binding.position + binding.length) {
                    scroll(offset() + (point < binding.position ? -binding.page : binding.page)); return;
                }
                binding.pointer = event.pointerId; binding.anchor = point - binding.position; track.setPointerCapture(event.pointerId);
            });
            track.addEventListener('pointermove', event => {
                if (event.pointerId !== binding.pointer) return;
                event.preventDefault(); event.stopPropagation();
                if (disabled()) finish(); else drag(event);
            });
            track.addEventListener('pointerup', event => {
                if (event.pointerId !== binding.pointer) return;
                event.preventDefault(); event.stopPropagation(); if (!disabled()) drag(event); finish();
            });
            track.addEventListener('pointercancel', finish); track.addEventListener('lostpointercapture', finish);
            track.addEventListener('keydown', event => {
                if (disabled()) return;
                let next = offset();
                if (event.key === 'Home') next = 0;
                else if (event.key === 'End') next = binding.limit;
                else if (event.key === 'PageUp') next -= binding.page;
                else if (event.key === 'PageDown') next += binding.page;
                else if (event.key === (binding.axis === 0 ? 'ArrowLeft' : 'ArrowUp')) next -= 40;
                else if (event.key === (binding.axis === 0 ? 'ArrowRight' : 'ArrowDown')) next += 40;
                else return;
                event.preventDefault(); event.stopPropagation(); scroll(next);
            });
            binding.update = update;
            binding.scrolled = update;
            viewport.addEventListener('scroll', update);
            binding.observer = new ResizeObserver(update);
            for (const element of [viewport, viewport.firstElementChild, track, thumb]) if (element) binding.observer.observe(element);
            scrollBindings.set(track, binding);
        }
        binding.update();
    }
    function authorSlots(root, classes) {
        const elements = [...root.children];
        const slots = classes.map(name => {
            const matches = elements.filter(element => element.classList.contains(name));
            if (matches.length > 1) throw new Error('Duplicate author slot: ' + name);
            return matches[0] || null;
        });
        if (slots.some(Boolean) && (slots.some(slot => !slot) || new Set(slots).size !== slots.length))
            throw new Error('Author controls require complete, distinct slots.');
        if ([...root.childNodes].some(child => child.nodeType === Node.ELEMENT_NODE ? !slots.includes(child)
            : child.nodeType === Node.TEXT_NODE && /[^\t\n\f\r ]/.test(child.data)))
            throw new Error('Author content must be inside its explicit control slot.');
        return slots.some(Boolean) ? slots : null;
    }
    function refreshFoldout(root) {
        let binding = foldouts.get(root);
        if (!binding) {
            let slots = authorSlots(root, ['urui-foldout-header', 'urui-foldout-content']);
            if (!slots) {
                const header = root.ownerDocument.createElement('button'), label = root.ownerDocument.createElement('span'), content = root.ownerDocument.createElement('div');
                header.className = 'urui-foldout-header'; label.className = 'urui-foldout-label'; content.className = 'urui-foldout-content';
                content.style.display = 'block'; root.style.display = 'block';
                header.append(label); root.append(header, content); slots = [header, content];
            }
            const [header, content] = slots;
            if (header.tagName !== 'BUTTON') throw new Error('urui-foldout-header must be a button.');
            binding = { header, content, label: header.querySelector('.urui-foldout-label') || header, display: content.style.display };
            const setValue = expanded => {
                if (root.closest('[disabled]')) return;
                const previous = root.getAttribute('data-urui-expanded') !== 'false';
                root.setAttribute('data-urui-expanded', String(expanded)); refreshFoldout(root);
                if (previous !== expanded) root.dispatchEvent(new CustomEvent('change', { bubbles: true, detail: { previousValue: previous, newValue: expanded } }));
            };
            header.addEventListener('click', () => setValue(root.getAttribute('data-urui-expanded') === 'false'));
            header.addEventListener('keydown', event => {
                if (event.target !== header || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
                event.preventDefault(); setValue(event.key === 'ArrowRight');
            });
            foldouts.set(root, binding);
        }
        const expanded = root.getAttribute('data-urui-expanded') !== 'false';
        if (!expanded && binding.content.contains(root.ownerDocument.activeElement)) binding.header.focus();
        const display = expanded ? binding.display : 'none';
        if (binding.content.style.display !== display) binding.content.style.display = display;
        const caption = root.getAttribute('data-urui-label');
        if (caption !== null && binding.label.textContent !== caption) binding.label.textContent = caption;
        root.classList.toggle('urui-expanded', expanded); binding.header.setAttribute('aria-expanded', String(expanded));
        if (binding.content.id) binding.header.setAttribute('aria-controls', binding.content.id);
    }
    function refreshProgress(root) {
        let binding = progressBars.get(root);
        if (!binding) {
            let slots = authorSlots(root, ['urui-progress-fill', 'urui-progress-title', 'urui-progress-content']);
            if (!slots) {
                const fill = root.ownerDocument.createElement('div'), title = root.ownerDocument.createElement('label'), content = root.ownerDocument.createElement('div');
                fill.className = 'urui-progress-fill'; title.className = 'urui-progress-title'; content.className = 'urui-progress-content';
                Object.assign(fill.style, { height: '100%', position: 'absolute' }); title.style.position = 'relative';
                root.append(fill, title, content); slots = [fill, title, content];
            }
            binding = { fill: slots[0], title: slots[1] }; progressBars.set(root, binding);
        }
        const read = (name, fallback) => {
            const raw = root.getAttribute(name);
            if (raw === null) return fallback;
            if (!/^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/.test(raw.trim()) || !Number.isFinite(Math.fround(Number(raw))))
                throw new Error('URUI progress ' + name + ' must be a finite number.');
            return Math.fround(Number(raw));
        };
        const low = read('min', 0), high = read('max', 100);
        if (high < low) throw new Error('URUI progress max must not be below min.');
        const value = Math.max(low, Math.min(high, read('value', 0)));
        binding.fill.style.width = (high > low ? Math.fround((value - low) / (high - low) * 100) : 0) + '%';
        const caption = root.getAttribute('data-urui-label');
        if (caption !== null && binding.title.textContent !== caption) binding.title.textContent = caption;
        root.setAttribute('aria-valuemin', String(low)); root.setAttribute('aria-valuemax', String(high)); root.setAttribute('aria-valuenow', String(value));
    }
    function refresh(root = document) {
        const slider = root.closest?.('urui-slider');
        if (slider) refreshSlider(slider);
        for (const element of root.querySelectorAll('urui-slider')) refreshSlider(element);
        const select = root.closest?.('urui-select');
        if (select) refreshSelect(select);
        for (const element of root.querySelectorAll('urui-select')) refreshSelect(element);
        const viewport = root.closest?.('urui-scroll-view');
        if (viewport) refreshScrollView(viewport);
        for (const element of root.querySelectorAll('urui-scroll-view')) refreshScrollView(element);
        const scrollbar = root.closest?.('.urui-scrollbar');
        if (scrollbar) refreshScrollbar(scrollbar);
        for (const element of root.querySelectorAll('.urui-scrollbar')) refreshScrollbar(element);
        for (const [track, binding] of scrollBindings)
            if (root === binding.viewport || binding.viewport.contains(root)) binding.update();
        const foldout = root.closest?.('urui-foldout');
        if (foldout) refreshFoldout(foldout);
        for (const element of root.querySelectorAll('urui-foldout')) refreshFoldout(element);
        const progress = root.closest?.('urui-progress-bar');
        if (progress) refreshProgress(progress);
        for (const element of root.querySelectorAll('urui-progress-bar')) refreshProgress(element);
    }
    const observer = new MutationObserver(records => {
        for (const record of records) {
            if (record.type === 'childList') {
                for (const node of record.addedNodes) if (node.nodeType === Node.ELEMENT_NODE) refresh(node);
            } else {
                const owner = record.target.nodeType === Node.ELEMENT_NODE ? record.target : record.target.parentElement;
                if (owner) refresh(owner);
            }
        }
        for (const [track, binding] of scrollBindings) if (!track.isConnected) {
            binding.viewport.removeEventListener('scroll', binding.scrolled);
            binding.observer.disconnect(); scrollBindings.delete(track);
        }
    });
    // Repeated tool attachment reuses the existing observer and control bindings.
    if (globalThis.URUI?.preview) { globalThis.URUI.preview.refresh(); return; }
    globalThis.URUI ??= {};
    globalThis.URUI.preview = Object.freeze({ refresh });
    const start = () => {
        refresh();
        observer.observe(document.documentElement, { subtree: true, childList: true, characterData: true, attributes: true,
            attributeFilter: ['min', 'max', 'step', 'value', 'disabled', 'selected', 'label', 'data-urui-orientation', 'data-urui-label', 'data-urui-expanded'] });
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
