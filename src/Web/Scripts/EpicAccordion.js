/*
 * Restores Epic's MUI accordion behavior in static/offline pages.
 */
(function () {
	if (window.__mhtmlEpicAccordionReady) return;
	window.__mhtmlEpicAccordionReady = true;

	injectStyle();
	setupAccordions();
	expandHashTarget();
	window.addEventListener("hashchange", () => {
		setTimeout(() => expandHashTarget(true), 0);
	});

	function injectStyle() {
		if (document.getElementById("mhtml-epic-accordion-style")) return;
		const style = document.createElement("style");
		style.id = "mhtml-epic-accordion-style";
		style.textContent = `
			.MuiAccordion-root.mhtml-accordion-ready > .MuiAccordionSummary-root {
				cursor: pointer;
				user-select: none;
			}
			.MuiAccordion-root.mhtml-accordion-ready > .MuiAccordionSummary-root:focus-visible {
				outline: 2px solid #58c7ff;
				outline-offset: 4px;
			}
			.MuiAccordion-root.mhtml-accordion-ready > .MuiCollapse-root.MuiCollapse-entered {
				height: auto !important;
				overflow: visible !important;
				visibility: visible !important;
			}
			.MuiAccordion-root.mhtml-accordion-ready > .MuiCollapse-root.MuiCollapse-hidden {
				height: 0 !important;
				overflow: hidden !important;
				padding-bottom: 0 !important;
				visibility: hidden !important;
			}
			.MuiAccordion-root.mhtml-accordion-ready .MuiAccordionSummary-expandIconWrapper svg {
				transition: transform 180ms cubic-bezier(0.4, 0, 0.2, 1);
			}
			.MuiAccordion-root.mhtml-accordion-ready .MuiAccordionSummary-expandIconWrapper.Mui-expanded svg {
				transform: rotate(180deg);
			}
		`;
		document.head.appendChild(style);
	}

	function setupAccordions() {
		const roots = Array.from(document.querySelectorAll(".accordion__wrapper > .MuiAccordion-root, .MuiAccordion-root"));
		let index = 0;
		for (const root of roots) {
			const summary = findDirectChild(root, "MuiAccordionSummary-root");
			const collapse = findDirectChild(root, "MuiCollapse-root");
			if (!summary || !collapse || root.dataset.mhtmlAccordionReady === "true") continue;

			index++;
			root.dataset.mhtmlAccordionReady = "true";
			root.classList.add("mhtml-accordion-ready");
			prepareAccessibility(root, summary, collapse, index);

			const expanded = readInitialExpanded(root, summary, collapse);
			setExpanded(root, expanded);

			summary.addEventListener("click", event => {
				if (event.defaultPrevented || shouldIgnoreClick(event, summary)) return;
				toggle(root);
			});
			summary.addEventListener("keydown", event => {
				if (event.key !== "Enter" && event.key !== " ") return;
				event.preventDefault();
				toggle(root);
			});
		}
	}

	function prepareAccessibility(root, summary, collapse, index) {
		if (!summary.hasAttribute("tabindex")) summary.setAttribute("tabindex", "0");
		if (!summary.hasAttribute("role")) summary.setAttribute("role", "button");

		const title = summary.querySelector(".accordion__title[id], [id]");
		const baseId = title?.id || `mhtml-accordion-${index}`;
		if (!summary.id) summary.id = `${baseId}-summary`;

		const region = collapse.querySelector('[role="region"], .MuiAccordion-region');
		const panel = region || collapse;
		if (!panel.id) panel.id = `${baseId}-panel`;
		if (!panel.hasAttribute("role")) panel.setAttribute("role", "region");
		panel.setAttribute("aria-labelledby", summary.id);
		summary.setAttribute("aria-controls", panel.id);
	}

	function readInitialExpanded(root, summary, collapse) {
		if (summary.getAttribute("aria-expanded") === "true") return true;
		if (root.classList.contains("Mui-expanded")) return true;
		return collapse.classList.contains("MuiCollapse-entered")
			&& !collapse.classList.contains("MuiCollapse-hidden");
	}

	function toggle(root) {
		const summary = findDirectChild(root, "MuiAccordionSummary-root");
		if (!summary) return;
		setExpanded(root, summary.getAttribute("aria-expanded") !== "true");
	}

	function setExpanded(root, expanded) {
		const summary = findDirectChild(root, "MuiAccordionSummary-root");
		const collapse = findDirectChild(root, "MuiCollapse-root");
		if (!summary || !collapse) return;

		root.classList.toggle("Mui-expanded", expanded);
		summary.classList.toggle("Mui-expanded", expanded);
		summary.setAttribute("aria-expanded", expanded ? "true" : "false");
		summary.querySelector(".MuiAccordionSummary-content")?.classList.toggle("Mui-expanded", expanded);
		summary.querySelector(".MuiAccordionSummary-expandIconWrapper")?.classList.toggle("Mui-expanded", expanded);

		collapse.classList.toggle("MuiCollapse-entered", expanded);
		collapse.classList.toggle("MuiCollapse-hidden", !expanded);
		collapse.setAttribute("aria-hidden", expanded ? "false" : "true");
		collapse.style.height = expanded ? "auto" : "0px";
		collapse.style.overflow = expanded ? "visible" : "hidden";
		collapse.style.visibility = expanded ? "visible" : "hidden";
		if (!collapse.style.minHeight) collapse.style.minHeight = "0px";
	}

	function expandHashTarget(scrollAfterOpen) {
		const target = findHashTarget();
		if (!target) return;

		let opened = false;
		for (const root of findAccordionAncestors(target)) {
			const summary = findDirectChild(root, "MuiAccordionSummary-root");
			if (summary?.getAttribute("aria-expanded") === "true") continue;
			setExpanded(root, true);
			opened = true;
		}

		if (opened && scrollAfterOpen) {
			requestAnimationFrame(() => target.scrollIntoView());
		}
	}

	function findAccordionAncestors(target) {
		const roots = [];
		let cursor = target;
		while (cursor && cursor !== document.documentElement) {
			const root = cursor.closest(".MuiAccordion-root.mhtml-accordion-ready");
			if (!root) break;
			if (!roots.includes(root)) roots.unshift(root);
			cursor = root.parentElement;
		}
		return roots;
	}

	function findHashTarget() {
		const raw = location.hash ? location.hash.slice(1) : "";
		if (!raw) return null;

		const decoded = safeDecode(raw);
		return document.getElementById(decoded)
			|| document.getElementById(raw)
			|| findByName(decoded)
			|| findByName(raw);
	}

	function findByName(value) {
		if (!value) return null;
		return document.querySelector(`[name="${escapeAttributeValue(value)}"]`);
	}

	function safeDecode(value) {
		try {
			return decodeURIComponent(value);
		} catch {
			return value;
		}
	}

	function escapeAttributeValue(value) {
		return String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"');
	}

	function findDirectChild(root, className) {
		for (const child of root.children) {
			if (child.classList.contains(className)) return child;
		}
		return null;
	}

	function shouldIgnoreClick(event, summary) {
		const interactive = event.target.closest("a, button, input, select, textarea, label");
		return interactive && interactive !== summary;
	}
})();
