/*
 * Restores Unreal Blueprint snippets/renders in archived Epic documentation.
 */
(function () {
	if (window.__mhtmlEpicBlueprintReady) return;
	window.__mhtmlEpicBlueprintReady = true;

	// Load the mirrored blueprint assets through the workspace-backed media host.
	const jsUrls = [
		"https://media.local/assets/epic/blueprint_render.min.js"
	];
	const cssUrls = [
		"https://media.local/assets/epic/blueprint_render.min.css"
	];
	const defaultHeight = "55vh";
	let libraryPromise = null;

	injectStyle();
	setupBlueprintSnippets();
	setupBlueprintRenders();
	setupBlueprintCopyButtons();

	function injectStyle() {
		// Keep a single shared stylesheet even when the script is injected after multiple navigations.
		if (document.getElementById("mhtml-epic-blueprint-style")) return;
		const style = document.createElement("style");
		style.id = "mhtml-epic-blueprint-style";
		style.textContent = `
			blueprint-render.mhtml-blueprint-ready,
			blueprint-render.mhtml-blueprint-pending,
			blueprint-render.mhtml-blueprint-missing {
				display: block;
				width: 100%;
				margin: 0;
			}
			blueprint-render .blueprint-render {
				display: block;
				position: relative;
				width: 100%;
				min-height: 360px;
				height: var(--blueprint-height, ${defaultHeight});
				overflow: hidden;
				border-radius: 8px;
				background: #17181d;
			}
			block-code-snippet.mhtml-code-ready blueprint-render .blueprint-render {
				margin: 0;
			}
			block-snippet-md.mhtml-blueprint-snippet-ready,
			block-snippet.mhtml-blueprint-snippet-ready,
			.block-snippet.mhtml-blueprint-snippet-ready {
				display: block;
				margin: 1.25rem 0 1.5rem;
			}
			block-snippet-md.mhtml-blueprint-snippet-ready blueprint-render,
			block-snippet.mhtml-blueprint-snippet-ready blueprint-render,
			.block-snippet.mhtml-blueprint-snippet-ready blueprint-render {
				margin: 0 0 .75rem;
			}
			.mhtml-blueprint-message {
				display: flex;
				align-items: center;
				min-height: 120px;
				padding: 18px;
				border: 1px solid #3b3c42;
				border-radius: 8px;
				background: #17181d;
				color: #cfd3dc;
				font: 600 13px/1.5 inherit;
			}
			.bue-render.mhtml-static-blueprint .frame {
				cursor: grab;
			}
			.bue-render.mhtml-static-blueprint.is-dragging .frame {
				cursor: grabbing;
			}
			.bue-render.mhtml-static-blueprint .node.selected {
				outline: 2px solid rgba(0, 174, 255, .9);
				outline-offset: 2px;
			}
		`;
		document.head.appendChild(style);
	}

	function setupBlueprintSnippets() {
		// Some captures preserve only a hidden code block; insert a blueprint-render node before it.
		const snippets = document.querySelectorAll(
			'block-snippet-md[snippet-type="blueprint"], block-snippet[snippet-type="blueprint"], .block-snippet[snippet-type="blueprint"]'
		);
		for (const snippet of snippets) {
			if (snippet.querySelector("blueprint-render")) continue;
			const code = readBlueprintCodeFromSnippet(snippet);
			if (!code) continue;

			const render = document.createElement("blueprint-render");
			const target = document.createElement("div");
			target.className = "blueprint-render";
			render.appendChild(target);
			render.dataset.mhtmlBlueprintCode = code;

			const hiddenCode = snippet.querySelector(".visually-hidden");
			snippet.insertBefore(render, hiddenCode || snippet.firstChild);
			snippet.classList.add("mhtml-blueprint-snippet-ready");
		}
	}

	function setupBlueprintRenders() {
		const renders = Array.from(document.querySelectorAll("blueprint-render"));
		if (!renders.length) return;
		ensureBlueprintCss().catch(() => {});

		for (const render of renders) {
			setupBlueprintRender(render);
		}
	}

	function setupBlueprintCopyButtons() {
		const snippets = document.querySelectorAll(
			"block-code-snippet, block-snippet-md, block-snippet, .block-snippet"
		);
		for (const snippet of snippets) {
			if (!isBlueprintSnippet(snippet)) continue;
			wireBlueprintCopyButton(snippet);
		}
	}

	function isBlueprintSnippet(snippet) {
		const type = (snippet.getAttribute("snippet-type") || "").toLowerCase();
		const header = snippet.querySelector(".block-code-snippet-header-type")?.textContent || "";
		return type === "blueprint"
			|| /blueprint/i.test(header)
			|| !!snippet.querySelector("blueprint-render");
	}

	function wireBlueprintCopyButton(snippet) {
		const button = findBlueprintCopyButton(snippet) || createBlueprintCopyButton(snippet);
		if (!button || button.dataset.mhtmlBlueprintCopyReady === "true") return;

		button.dataset.mhtmlBlueprintCopyReady = "true";
		button.dataset.mhtmlBlueprintOriginalLabel = readCopyButtonLabel(button) || "Copy full snippet";
		button.dataset.mhtmlBlueprintSuffix = button.querySelector(".ps-1")?.textContent || "";
		button.addEventListener("click", async event => {
			event.preventDefault();
			event.stopPropagation();
			if (event.stopImmediatePropagation) event.stopImmediatePropagation();

			const source = readBlueprintCopySource(snippet);
			if (!source) {
				flashCopyButton(button, "No source found");
				return;
			}

			await copyTextToClipboard(source);
			flashCopyButton(button, "Copied");
		}, true);
	}

	function findBlueprintCopyButton(snippet) {
		const buttons = Array.from(snippet.querySelectorAll("button"));
		return buttons.find(button => /copy/i.test(button.textContent || "")) || null;
	}

	function createBlueprintCopyButton(snippet) {
		if (!snippet.matches("block-code-snippet")) return null;

		let actions = snippet.querySelector(".block-code-snippet-actions");
		if (!actions) {
			actions = document.createElement("div");
			actions.className = "block-code-snippet-actions";
			snippet.appendChild(actions);
		}

		const button = document.createElement("button");
		button.type = "button";
		button.className = "btn btn-secondary btn-sm";
		const icon = document.createElement("span");
		icon.className = "eds-icon icon-stacked-squares icon-default-width icon-pad-right";
		button.appendChild(icon);
		button.appendChild(document.createTextNode(" Copy full snippet"));
		actions.appendChild(button);
		return button;
	}

	function readBlueprintCopySource(snippet) {
		const selectors = [
			"textarea.mhtml-full-snippet-source",
			"textarea[data-mhtml-full-source='true']",
			"textarea[data-blueprint-code]",
			".visually-hidden",
			"textarea"
		];

		for (const selector of selectors) {
			const element = snippet.querySelector(selector);
			if (!element) continue;
			const source = normalizeBlueprintCode("value" in element ? element.value : element.textContent);
			if (looksLikeBlueprintCode(source)) return source;
		}

		const render = snippet.querySelector("blueprint-render");
		return render ? readBlueprintCode(render) : readBlueprintCodeFromSnippet(snippet);
	}

	function readCopyButtonLabel(button) {
		return Array.from(button.childNodes)
			.filter(node => node.nodeType === Node.TEXT_NODE)
			.map(node => node.textContent || "")
			.join("")
			.trim();
	}

	function flashCopyButton(button, label) {
		const original = button.dataset.mhtmlBlueprintOriginalLabel || "Copy full snippet";
		setCopyButtonLabel(button, label);
		clearTimeout(button.__mhtmlBlueprintCopyTimer);
		button.__mhtmlBlueprintCopyTimer = setTimeout(() => {
			setCopyButtonLabel(button, original);
		}, 1200);
	}

	function setCopyButtonLabel(button, label) {
		const icon = findCopyButtonIcon(button);
		const suffix = button.dataset.mhtmlBlueprintSuffix || "";
		button.textContent = "";
		if (icon) button.appendChild(icon);
		button.appendChild(document.createTextNode(icon ? " " + label : label));
		if (suffix && /copy/i.test(label)) {
			const span = document.createElement("span");
			span.className = "ps-1";
			span.textContent = suffix;
			button.appendChild(span);
		}
	}

	function findCopyButtonIcon(button) {
		const icon = button.querySelector(".eds-icon");
		if (!icon) return null;
		const wrapper = icon.parentElement;
		if (wrapper && wrapper !== button && wrapper.children.length === 1) {
			return wrapper.cloneNode(true);
		}
		return icon.cloneNode(true);
	}

	async function copyTextToClipboard(text) {
		try {
			await navigator.clipboard.writeText(text);
			return;
		} catch {
		}

		const input = document.createElement("textarea");
		input.value = text;
		input.style.position = "fixed";
		input.style.left = "-100000px";
		input.style.top = "0";
		document.body.appendChild(input);
		input.focus();
		input.select();
		document.execCommand("copy");
		input.remove();
	}

	function setupBlueprintRender(render) {
		if (render.dataset.mhtmlBlueprintReady === "true") return;

		const target = ensureTarget(render);
		const existingRender = target.querySelector(".bue-render .frame, .frame[data-parse_blueprint]");
		if (existingRender) {
			render.dataset.mhtmlBlueprintReady = "true";
			render.classList.add("mhtml-blueprint-ready");
			applyBlueprintHeight(render, target);
			bindStaticBlueprintInteractions(target);
			return;
		}

		const code = readBlueprintCode(render);
		if (!code) {
			render.dataset.mhtmlBlueprintReady = "true";
			render.classList.add("mhtml-blueprint-missing");
			showMessage(target, "Blueprint data is not available in this MHTML snapshot.");
			return;
		}

		render.classList.add("mhtml-blueprint-pending");
		showMessage(target, "Loading Blueprint renderer...");
		ensureBlueprintLibrary()
			.then(() => renderBlueprint(render, target, code))
			.catch(() => {
				render.dataset.mhtmlBlueprintReady = "true";
				render.classList.remove("mhtml-blueprint-pending");
				render.classList.add("mhtml-blueprint-missing");
				showMessage(target, "Blueprint renderer library could not be loaded.");
			});
	}

	function ensureTarget(render) {
		let target = render.querySelector(":scope > .blueprint-render");
		if (!target) {
			target = document.createElement("div");
			target.className = "blueprint-render";
			render.appendChild(target);
		}
		return target;
	}

	function renderBlueprint(render, target, code) {
		// Rendering depends on Epic's blueprint library, which may load from local assets or the CDN.
		if (!window.blueprintUE?.render?.Main) throw new Error("BlueprintUE renderer is unavailable.");

		if (render.__mhtmlBlueprintRenderer?.stop) {
			try {
				render.__mhtmlBlueprintRenderer.stop();
			} catch {
				// Best effort only; the old renderer may already be detached.
			}
		}

		target.textContent = "";
		applyBlueprintHeight(render, target);
		render.__mhtmlBlueprintRenderer = new window.blueprintUE.render.Main(code, target, {
			height: readBlueprintHeight(render, target)
		});
		render.__mhtmlBlueprintRenderer.start();
		render.dataset.mhtmlBlueprintReady = "true";
		render.classList.remove("mhtml-blueprint-pending", "mhtml-blueprint-missing");
		render.classList.add("mhtml-blueprint-ready");
	}

	function applyBlueprintHeight(render, target) {
		const height = readBlueprintHeight(render, target);
		target.style.height = height;
		target.style.minHeight = height;
		const frame = target.querySelector(".frame");
		if (frame && !frame.style.height) frame.style.height = height;
	}

	function readBlueprintHeight(render, target) {
		const height = getComputedStyle(render).getPropertyValue("--blueprint-height").trim()
			|| getComputedStyle(target).getPropertyValue("--blueprint-height").trim();
		return height || defaultHeight;
	}

	function readBlueprintCode(render) {
		const direct = normalizeBlueprintCode(
			render.dataset.mhtmlBlueprintCode
			|| render.getAttribute("code")
			|| render.getAttribute("data-code")
			|| ""
		);
		if (looksLikeBlueprintCode(direct)) return direct;

		const snippet = render.closest("block-code-snippet, block-snippet-md, block-snippet, .block-snippet");
		return readBlueprintCodeFromSnippet(snippet);
	}

	function readBlueprintCodeFromSnippet(snippet) {
		if (!snippet) return "";

		const candidates = [
			...snippet.querySelectorAll(
				"textarea, .visually-hidden, [data-blueprint-code], pre, code"
			)
		];
		for (const candidate of candidates) {
			const text = normalizeBlueprintCode("value" in candidate ? candidate.value : candidate.textContent);
			if (looksLikeBlueprintCode(text)) return text;
		}

		const clone = snippet.cloneNode(true);
		for (const removable of clone.querySelectorAll(
			"blueprint-render, .blueprint-render, .block-code-snippet-actions, .block-code-snippet-header, button"
		)) {
			removable.remove();
		}
		const text = normalizeBlueprintCode(clone.textContent || "");
		return looksLikeBlueprintCode(text) ? text : "";
	}

	function normalizeBlueprintCode(value) {
		return (value || "")
			.replace(/\u00a0/g, " ")
			.replace(/\r\n?/g, "\n")
			.replace(/[ \t]+\n/g, "\n")
			.trim();
	}

	function looksLikeBlueprintCode(value) {
		return /Begin Object|End Object|CustomProperties Pin|K2Node_|EdGraphNode/.test(value || "");
	}

	function showMessage(target, message) {
		target.innerHTML = "";
		const box = document.createElement("div");
		box.className = "mhtml-blueprint-message";
		box.textContent = message;
		target.appendChild(box);
	}

	function bindStaticBlueprintInteractions(target) {
		// Static blueprint canvases get pan/zoom/fullscreen controls so large graphs remain usable offline.
		const root = target.querySelector(".bue-render");
		const frame = target.querySelector(".frame");
		const canvas = target.querySelector(".canvas");
		const reference = target.querySelector(".reference");
		if (!root || !frame || !canvas || !reference) return;
		if (root.dataset.mhtmlStaticInteractions === "true") return;

		root.dataset.mhtmlStaticInteractions = "true";
		root.classList.add("mhtml-static-blueprint");
		ensureStaticHeader(frame);

		const initial = parseTransform(canvas.style.transform || getComputedStyle(canvas).transform);
		const state = {
			x: initial.x,
			y: initial.y,
			scale: initial.scale,
			resetX: initial.x,
			resetY: initial.y,
			resetScale: initial.scale,
			dragging: false,
			pointerId: null,
			startClientX: 0,
			startClientY: 0,
			startX: initial.x,
			startY: initial.y,
			frameHeight: frame.style.height || getComputedStyle(frame).height || defaultHeight
		};

		applyStaticTransform(canvas, reference, state);
		updateStaticZoom(root, state.scale);

		frame.addEventListener("pointerdown", event => {
			if (event.target.closest(".frame-header")) return;
			if (event.button !== 0 && event.button !== 1 && event.button !== 2) return;

			state.dragging = true;
			state.pointerId = event.pointerId;
			state.startClientX = event.clientX;
			state.startClientY = event.clientY;
			state.startX = state.x;
			state.startY = state.y;
			root.classList.add("is-dragging");
			frame.setPointerCapture(event.pointerId);
			event.preventDefault();
		});

		frame.addEventListener("pointermove", event => {
			if (!state.dragging || state.pointerId !== event.pointerId) return;
			state.x = state.startX + event.clientX - state.startClientX;
			state.y = state.startY + event.clientY - state.startClientY;
			applyStaticTransform(canvas, reference, state);
			event.preventDefault();
		});

		const endDrag = event => {
			if (!state.dragging || state.pointerId !== event.pointerId) return;
			state.dragging = false;
			state.pointerId = null;
			root.classList.remove("is-dragging");
			try {
				frame.releasePointerCapture(event.pointerId);
			} catch {
				// Pointer capture may already be released when leaving fullscreen.
			}
		};
		frame.addEventListener("pointerup", endDrag);
		frame.addEventListener("pointercancel", endDrag);
		frame.addEventListener("contextmenu", event => event.preventDefault());

		frame.addEventListener("wheel", event => {
			if (!event.ctrlKey) return;
			const delta = event.deltaY < 0 ? 1.12 : 1 / 1.12;
			zoomStaticBlueprint(frame, canvas, reference, root, state, event.clientX, event.clientY, delta);
			event.preventDefault();
		}, { passive: false });

		frame.addEventListener("click", event => {
			const node = event.target.closest(".node");
			if (!node || !frame.contains(node)) return;
			if (!event.ctrlKey && !event.metaKey) {
				for (const selected of frame.querySelectorAll(".node.selected")) selected.classList.remove("selected");
			}
			node.classList.toggle("selected");
		});

		frame.querySelector(".frame-header__buttons")?.addEventListener("click", event => {
			const button = event.target.closest(".frame-header__buttons-fullscreen, .frame-header__buttons-reset, .frame-header__buttons-panel");
			if (!button) return;
			if (button.classList.contains("frame-header__buttons-fullscreen")) {
				toggleStaticFullscreen(root, frame, button, state);
			} else if (button.classList.contains("frame-header__buttons-reset")) {
				resetStaticBlueprint(canvas, reference, root, state);
			} else if (button.classList.contains("frame-header__buttons-panel")) {
				toggleStaticPanel(root);
			}
			event.preventDefault();
			event.stopPropagation();
		});

		document.addEventListener("fullscreenchange", () => {
			if (document.fullscreenElement === root) return;
			const button = frame.querySelector(".frame-header__buttons-fullscreen");
			button?.classList.remove("frame-header__buttons-fullscreen--exit");
			if (state.frameHeight) frame.style.height = state.frameHeight;
		});
	}

	function ensureStaticHeader(frame) {
		let header = frame.querySelector(":scope > .frame-header");
		if (!header) {
			header = document.createElement("div");
			header.className = "frame-header";
			frame.appendChild(header);
		}

		if (!header.querySelector(".frame-header__buttons")) {
			const buttons = document.createElement("div");
			buttons.className = "frame-header__buttons";
			header.insertBefore(buttons, header.firstChild);
		}
		const buttons = header.querySelector(".frame-header__buttons");
		ensureStaticHeaderButton(buttons, "frame-header__buttons-fullscreen", "Fullscreen");
		ensureStaticHeaderButton(buttons, "frame-header__buttons-reset", "Reset");

		if (!header.querySelector(".frame-header__breadcrumb")) {
			const breadcrumb = document.createElement("div");
			breadcrumb.className = "frame-header__breadcrumb";
			const item = document.createElement("span");
			item.className = "frame-header__breadcrumb-item";
			item.textContent = "Graph";
			breadcrumb.appendChild(item);
			header.appendChild(breadcrumb);
		}

		if (!header.querySelector(".frame-header__current-zoom")) {
			const zoom = document.createElement("div");
			zoom.className = "frame-header__current-zoom";
			zoom.textContent = "Zoom 1:1";
			header.appendChild(zoom);
		}

		if (!frame.querySelector(":scope > .overlay")) {
			const overlay = document.createElement("div");
			overlay.className = "overlay";
			overlay.style.display = "none";
			frame.appendChild(overlay);
		}
	}

	function ensureStaticHeaderButton(parent, className, text) {
		if (!parent || parent.querySelector("." + className)) return;
		const button = document.createElement("div");
		button.className = className;
		button.textContent = text;
		parent.appendChild(button);
	}

	function parseTransform(value) {
		const fallback = { x: 0, y: 0, scale: 1 };
		if (!value || value === "none") return fallback;

		const translate = /translate\(\s*(-?\d+(?:\.\d+)?)px(?:,\s*(-?\d+(?:\.\d+)?)px)?\s*\)/.exec(value);
		const scale = /scale\(\s*(-?\d+(?:\.\d+)?)\s*\)/.exec(value);
		if (translate || scale) {
			return {
				x: translate ? Number(translate[1]) || 0 : 0,
				y: translate ? Number(translate[2]) || 0 : 0,
				scale: scale ? Number(scale[1]) || 1 : 1
			};
		}

		const matrix = /matrix\(\s*([^)]*)\)/.exec(value);
		if (!matrix) return fallback;
		const parts = matrix[1].split(",").map(part => Number(part.trim()));
		return {
			x: parts[4] || 0,
			y: parts[5] || 0,
			scale: parts[0] || 1
		};
	}

	function applyStaticTransform(canvas, reference, state) {
		const transform = `translate(${state.x}px, ${state.y}px) scale(${state.scale})`;
		canvas.style.transform = transform;
		reference.style.transform = transform;
	}

	function zoomStaticBlueprint(frame, canvas, reference, root, state, clientX, clientY, factor) {
		const oldScale = state.scale;
		const newScale = Math.max(0.04, Math.min(8, Number((oldScale * factor).toFixed(4))));
		if (newScale === oldScale) return;

		const rect = frame.getBoundingClientRect();
		const px = clientX - rect.left;
		const py = clientY - rect.top;
		const ratio = newScale / oldScale;

		state.x = px - (px - state.x) * ratio;
		state.y = py - (py - state.y) * ratio;
		state.scale = newScale;
		applyStaticTransform(canvas, reference, state);
		updateStaticZoom(root, state.scale);
		showStaticOverlay(frame, `Zoom ${formatStaticZoom(state.scale)}`);
	}

	function resetStaticBlueprint(canvas, reference, root, state) {
		state.x = state.resetX;
		state.y = state.resetY;
		state.scale = state.resetScale;
		applyStaticTransform(canvas, reference, state);
		updateStaticZoom(root, state.scale);
	}

	function updateStaticZoom(root, scale) {
		const zoom = root.querySelector(".frame-header__current-zoom");
		if (!zoom) return;
		zoom.classList.add("update");
		zoom.textContent = `Zoom ${formatStaticZoom(scale)}`;
		setTimeout(() => zoom.classList.remove("update"), 120);
	}

	function formatStaticZoom(scale) {
		if (Math.abs(scale - 1) < 0.001) return "1:1";
		return `${Math.round(scale * 100)}%`;
	}

	function showStaticOverlay(frame, text) {
		const overlay = frame.querySelector(":scope > .overlay");
		if (!overlay) return;
		overlay.textContent = text;
		overlay.style.display = "flex";
		clearTimeout(frame.__mhtmlBlueprintOverlayTimer);
		frame.__mhtmlBlueprintOverlayTimer = setTimeout(() => {
			overlay.style.display = "none";
		}, 700);
	}

	function toggleStaticFullscreen(root, frame, button, state) {
		if (document.fullscreenElement === root) {
			button.classList.remove("frame-header__buttons-fullscreen--exit");
			if (state.frameHeight) frame.style.height = state.frameHeight;
			document.exitFullscreen?.();
			return;
		}

		state.frameHeight = frame.style.height || getComputedStyle(frame).height || defaultHeight;
		root.requestFullscreen?.().then(() => {
			frame.style.height = `${screen.height}px`;
			button.classList.add("frame-header__buttons-fullscreen--exit");
		}).catch(() => {});
	}

	function toggleStaticPanel(root) {
		const panel = root.querySelector(".panel");
		if (!panel) return;
		panel.style.display = panel.style.display === "block" ? "none" : "block";
	}

	function ensureBlueprintLibrary() {
		if (window.blueprintUE?.render?.Main) return Promise.resolve();
		if (!libraryPromise) {
			libraryPromise = Promise.all([ensureBlueprintCss(), loadFirstScript(jsUrls)])
				.then(() => waitForBlueprintUE());
		}
		return libraryPromise;
	}

	function ensureBlueprintCss() {
		if (document.querySelector('link[data-mhtml-blueprint-css="true"]')) return Promise.resolve();
		const existing = Array.from(document.querySelectorAll('link[rel="stylesheet"]'))
			.find(link => (link.href || "").includes("blueprint_render.min.css"));
		if (existing) {
			existing.dataset.mhtmlBlueprintCss = "true";
			return Promise.resolve();
		}

		return loadFirstStylesheet(cssUrls);
	}

	function loadFirstStylesheet(urls) {
		let index = 0;
		const loadNext = () => {
			if (index >= urls.length) return Promise.reject(new Error("Failed to load BlueprintUE CSS."));
			return loadStylesheet(urls[index++]).catch(loadNext);
		};
		return loadNext();
	}

	function loadStylesheet(url) {
		return new Promise((resolve, reject) => {
			const link = document.createElement("link");
			link.rel = "stylesheet";
			link.href = url;
			link.dataset.mhtmlBlueprintCss = "true";
			link.onload = () => resolve();
			link.onerror = () => {
				link.remove();
				reject(new Error("Failed to load BlueprintUE CSS."));
			};
			document.head.appendChild(link);
		});
	}

	function loadFirstScript(urls) {
		let index = 0;
		const loadNext = () => {
			if (index >= urls.length) return Promise.reject(new Error("Failed to load BlueprintUE JS."));
			return loadScript(urls[index++]).catch(loadNext);
		};
		return loadNext();
	}

	function loadScript(url) {
		if (window.blueprintUE?.render?.Main) return Promise.resolve();
		const existing = Array.from(document.querySelectorAll("script[src]"))
			.find(script => (script.src || "").includes("blueprint_render.min.js"));
		if (existing) return waitForBlueprintUE();

		return new Promise((resolve, reject) => {
			const script = document.createElement("script");
			script.src = url;
			script.async = true;
			script.dataset.mhtmlBlueprintJs = "true";
			script.onload = () => resolve();
			script.onerror = () => {
				script.remove();
				reject(new Error("Failed to load BlueprintUE JS."));
			};
			document.head.appendChild(script);
		});
	}

	function waitForBlueprintUE() {
		if (window.blueprintUE?.render?.Main) return Promise.resolve();
		return new Promise((resolve, reject) => {
			const startedAt = performance.now();
			const timer = setInterval(() => {
				if (window.blueprintUE?.render?.Main) {
					clearInterval(timer);
					resolve();
				} else if (performance.now() - startedAt > 8000) {
					clearInterval(timer);
					reject(new Error("Timed out waiting for BlueprintUE."));
				}
			}, 100);
		});
	}
})();
