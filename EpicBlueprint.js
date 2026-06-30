(function () {
	if (window.__mhtmlEpicBlueprintReady) return;
	window.__mhtmlEpicBlueprintReady = true;

	const jsUrls = [
		"https://media.local/assets/epic/blueprint_render.min.js",
		"https://dev.epicgames.com/community/assets/javascript/blueprint_render.min.js"
	];
	const cssUrls = [
		"https://media.local/assets/epic/blueprint_render.min.css",
		"https://dev.epicgames.com/community/assets/styles/libs/blueprint_render.min.css"
	];
	const defaultHeight = "55vh";
	let libraryPromise = null;

	injectStyle();
	setupBlueprintSnippets();
	setupBlueprintRenders();

	function injectStyle() {
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
		`;
		document.head.appendChild(style);
	}

	function setupBlueprintSnippets() {
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

	function setupBlueprintRender(render) {
		if (render.dataset.mhtmlBlueprintReady === "true") return;

		const target = ensureTarget(render);
		const existingRender = target.querySelector(".bue-render .frame, .frame[data-parse_blueprint]");
		if (existingRender) {
			render.dataset.mhtmlBlueprintReady = "true";
			render.classList.add("mhtml-blueprint-ready");
			applyBlueprintHeight(render, target);
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
