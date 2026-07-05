/*
 * Restores Epic's image sequence slider for archived pages.
 */
(function () {
	if (window.__mhtmlEpicSliderSequenceReady) return;
	window.__mhtmlEpicSliderSequenceReady = true;

	injectStyle();
	setupSliderSequences();

	function injectStyle() {
		// Styles are injected once because this script can be reinjected after WebView navigation.
		if (document.getElementById("mhtml-epic-slider-sequence-style")) return;
		const style = document.createElement("style");
		style.id = "mhtml-epic-slider-sequence-style";
		style.textContent = `
			.block-slider-sequence.mhtml-slider-sequence-ready {
				display: block;
				margin: 1.25rem auto;
				max-width: 100%;
			}
			.block-slider-sequence.mhtml-slider-sequence-ready .img-list {
				display: block;
				position: relative;
				width: fit-content;
				max-width: 100%;
				margin: 0 auto;
			}
			.block-slider-sequence.mhtml-slider-sequence-ready .img {
				display: block;
				max-width: 100%;
				height: auto;
				margin: 0 auto;
			}
			.block-slider-sequence.mhtml-slider-sequence-ready .img.visually-hidden {
				display: none !important;
			}
			.block-slider-sequence.mhtml-slider-sequence-ready .form-range {
				display: block;
				width: 100%;
				margin-top: 1rem;
				cursor: pointer;
			}
			.block-slider-sequence.mhtml-slider-sequence-ready .caption {
				margin-top: .75rem;
				text-align: center;
			}
		`;
		document.head.appendChild(style);
	}

	function setupSliderSequences() {
		const sequences = document.querySelectorAll(
			"block-slider-sequence, block-slider-sequence-md, .block-slider-sequence"
		);
		for (const sequence of sequences) setupSequence(sequence);
	}

	function setupSequence(root) {
		// Each sequence owns one range input that switches visible images by index.
		if (root.dataset.mhtmlSliderSequenceReady === "true") return;
		const imgList = root.querySelector(".img-list");
		if (!imgList) return;

		const images = Array.from(imgList.querySelectorAll("img.img, img"));
		if (!images.length) return;

		root.dataset.mhtmlSliderSequenceReady = "true";
		root.classList.add("block-slider-sequence", "mhtml-slider-sequence-ready");

		const caption = root.querySelector(".caption")?.textContent?.trim() || images[0].getAttribute("alt") || "";
		normalizeImages(images, caption);

		const input = ensureRangeInput(root, imgList, images.length);
		const update = () => showImage(images, readIndex(input, images.length));

		input.addEventListener("input", update);
		input.addEventListener("change", update);
		preloadImages(root, images);
		applyInitialDimensions(images);
		update();
	}

	function normalizeImages(images, caption) {
		images.forEach((image, index) => {
			image.classList.add("img", `item-${index + 1}`, "img-fluid");
			if (caption && !image.getAttribute("alt")) image.setAttribute("alt", caption);
			image.setAttribute("loading", "lazy");
		});
	}

	function ensureRangeInput(root, imgList, count) {
		let input = root.querySelector("input[type='range']");
		if (!input) {
			input = document.createElement("input");
			input.type = "range";
			input.className = "mt-3 form-range";
			imgList.insertAdjacentElement("afterend", input);
		}

		input.min = "1";
		input.max = String(count);
		input.step = "1";
		if (!input.value || Number(input.value) < 1 || Number(input.value) > count) {
			input.value = "1";
		}
		input.classList.add("mt-3", "form-range");
		return input;
	}

	function showImage(images, activeIndex) {
		images.forEach((image, index) => {
			const active = index + 1 === activeIndex;
			image.classList.toggle("visually-hidden", !active);
			image.hidden = !active;
		});
	}

	function readIndex(input, count) {
		const value = input.valueAsNumber;
		const parsed = Number.isFinite(value) ? value : Number(input.value);
		if (!Number.isFinite(parsed)) return 1;
		return Math.max(1, Math.min(count, Math.round(parsed)));
	}

	function preloadImages(root, images) {
		// Preload all frames so dragging the range input does not flash unloaded images.
		const load = () => {
			for (const image of images) {
				const src = image.getAttribute("src");
				if (!src) continue;
				new Image().src = src;
				image.removeAttribute("loading");
			}
		};

		if (!("IntersectionObserver" in window)) {
			load();
			return;
		}

		const observer = new IntersectionObserver(entries => {
			if (!entries[0]?.isIntersecting) return;
			load();
			observer.disconnect();
		}, { threshold: .01 });
		observer.observe(root);
	}

	function applyInitialDimensions(images) {
		const first = images[0];
		const apply = () => {
			const width = first.naturalWidth || first.width || Number(first.getAttribute("width"));
			const height = first.naturalHeight || first.height || Number(first.getAttribute("height"));
			if (!width || !height) return;
			for (const image of images) {
				image.setAttribute("width", String(width));
				image.setAttribute("height", String(height));
			}
		};

		if (first.complete) {
			apply();
		} else {
			first.addEventListener("load", apply, { once: true });
		}
	}
})();
