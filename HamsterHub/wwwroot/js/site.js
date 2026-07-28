document.addEventListener("DOMContentLoaded", () => {
  const themeButton = document.querySelector("[data-theme-toggle]");
  themeButton?.addEventListener("click", () => {
    const nextTheme = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = nextTheme;
    localStorage.setItem("hamsterhub-theme", nextTheme);
  });

  const openDialog = window.hamsterHubDialog;
  const modalId = openDialog === "login"
    ? "loginModal"
    : openDialog === "signup"
      ? "signupModal"
      : null;

  if (modalId) {
    const modalElement = document.getElementById(modalId);
    if (modalElement) {
      bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }
  }

  document.querySelectorAll("[data-switch-modal]").forEach((button) => {
    button.addEventListener("click", () => {
      const currentModal = button.closest(".modal");
      const nextModal = document.getElementById(button.dataset.switchModal);

      if (!currentModal || !nextModal) {
        return;
      }

      const currentInstance = bootstrap.Modal.getOrCreateInstance(currentModal);
      currentModal.addEventListener("hidden.bs.modal", () => {
        bootstrap.Modal.getOrCreateInstance(nextModal).show();
      }, { once: true });
      currentInstance.hide();
    });
  });

  document.querySelectorAll("form[data-confirm]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
      }
    });
  });

  const dashboardPage = document.querySelector(".dashboard-shell, .kid-canvas");
  const dashboardScrollKey = "hamsterHub.dashboardScroll";

  if (dashboardPage) {
    try {
      const savedScroll = JSON.parse(sessionStorage.getItem(dashboardScrollKey));
      sessionStorage.removeItem(dashboardScrollKey);

      if (savedScroll?.path === window.location.pathname &&
          Number.isFinite(savedScroll.top) &&
          Date.now() - savedScroll.savedAt < 60000) {
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            window.scrollTo({ top: savedScroll.top, left: 0, behavior: "auto" });
          });
        });
      }
    } catch {
      // Continue normally when browser storage is unavailable or malformed.
    }

    document.querySelectorAll("form").forEach((form) => {
      form.addEventListener("submit", (event) => {
        if (event.defaultPrevented) {
          return;
        }

        try {
          sessionStorage.setItem(dashboardScrollKey, JSON.stringify({
            path: window.location.pathname,
            top: window.scrollY,
            savedAt: Date.now()
          }));
        } catch {
          // The action should still submit if browser storage is unavailable.
        }
      });
    });
  } else {
    try {
      sessionStorage.removeItem(dashboardScrollKey);
    } catch {
      // Browser storage may be unavailable in a restricted context.
    }
  }

  document.querySelectorAll("[data-dashboard-notice]").forEach((notice) => {
    let dismissTimer;
    let isDismissed = false;

    const dismissNotice = () => {
      if (isDismissed) {
        return;
      }

      isDismissed = true;
      window.clearTimeout(dismissTimer);
      notice.classList.add("is-leaving");
      window.setTimeout(() => notice.remove(), 220);
    };
    const scheduleDismissal = () => {
      if (isDismissed) {
        return;
      }

      window.clearTimeout(dismissTimer);
      dismissTimer = window.setTimeout(dismissNotice, 6000);
    };

    notice.addEventListener("click", dismissNotice);
    notice.querySelector("[data-dashboard-notice-close]")
      ?.addEventListener("click", (event) => {
        event.stopPropagation();
        dismissNotice();
      });
    notice.addEventListener("pointerenter", () => window.clearTimeout(dismissTimer));
    notice.addEventListener("pointerleave", scheduleDismissal);
    notice.addEventListener("focusin", () => window.clearTimeout(dismissTimer));
    notice.addEventListener("focusout", scheduleDismissal);
    scheduleDismissal();
  });

  const photoViewer = document.querySelector("[data-photo-viewer]");
  const photoViewerImage = photoViewer?.querySelector("[data-photo-viewer-image]");
  const photoViewerCaption = photoViewer?.querySelector("[data-photo-viewer-caption]");
  const photoViewerCount = photoViewer?.querySelector("[data-photo-viewer-count]");
  const previousPhotoButton = photoViewer?.querySelector("[data-photo-viewer-previous]");
  const nextPhotoButton = photoViewer?.querySelector("[data-photo-viewer-next]");
  let viewerPhotos = [];
  let viewerIndex = 0;
  let viewerTrigger = null;

  const renderViewerPhoto = () => {
    if (!photoViewerImage || viewerPhotos.length === 0) {
      return;
    }

    const photo = viewerPhotos[viewerIndex];
    photoViewerImage.src = photo.src;
    photoViewerImage.alt = photo.alt;
    if (photoViewerCaption) {
      photoViewerCaption.textContent = photo.alt;
    }
    if (photoViewerCount) {
      photoViewerCount.textContent = viewerPhotos.length > 1
        ? `${viewerIndex + 1} / ${viewerPhotos.length}`
        : "";
    }
    previousPhotoButton?.toggleAttribute("hidden", viewerPhotos.length < 2);
    nextPhotoButton?.toggleAttribute("hidden", viewerPhotos.length < 2);
  };

  const openPhotoViewer = (photos, trigger) => {
    if (!photoViewer || photos.length === 0) {
      return;
    }

    viewerPhotos = photos;
    viewerIndex = 0;
    viewerTrigger = trigger;
    renderViewerPhoto();
    photoViewer.hidden = false;
    document.body.classList.add("photo-viewer-open");
    photoViewer.querySelector("[data-photo-viewer-close]")?.focus();
  };

  const closePhotoViewer = () => {
    if (!photoViewer || photoViewer.hidden) {
      return;
    }

    photoViewer.hidden = true;
    document.body.classList.remove("photo-viewer-open");
    photoViewerImage?.removeAttribute("src");
    viewerTrigger?.focus();
    viewerTrigger = null;
    viewerPhotos = [];
  };

  document.querySelectorAll("[data-photo-viewer-src]").forEach((trigger) => {
    trigger.addEventListener("click", () => {
      openPhotoViewer([{
        src: trigger.dataset.photoViewerSrc,
        alt: trigger.dataset.photoViewerAlt || ""
      }], trigger);
    });
  });

  document.querySelectorAll("[data-photo-gallery]").forEach((trigger) => {
    trigger.addEventListener("click", () => {
      const photos = Array.from(trigger.querySelectorAll("[data-gallery-photo]"))
        .map((item) => ({
          src: item.dataset.src,
          alt: item.dataset.alt || ""
        }))
        .filter((item) => item.src);
      openPhotoViewer(photos, trigger);
    });
  });

  photoViewer?.querySelector("[data-photo-viewer-close]")
    ?.addEventListener("click", closePhotoViewer);
  photoViewer?.addEventListener("click", (event) => {
    if (event.target === photoViewer) {
      closePhotoViewer();
    }
  });
  previousPhotoButton?.addEventListener("click", () => {
    viewerIndex = (viewerIndex - 1 + viewerPhotos.length) % viewerPhotos.length;
    renderViewerPhoto();
  });
  nextPhotoButton?.addEventListener("click", () => {
    viewerIndex = (viewerIndex + 1) % viewerPhotos.length;
    renderViewerPhoto();
  });
  document.addEventListener("keydown", (event) => {
    if (!photoViewer || photoViewer.hidden) {
      return;
    }

    if (event.key === "Escape") {
      closePhotoViewer();
    } else if (event.key === "ArrowLeft" && viewerPhotos.length > 1) {
      previousPhotoButton?.click();
    } else if (event.key === "ArrowRight" && viewerPhotos.length > 1) {
      nextPhotoButton?.click();
    }
  });

  document.querySelectorAll(".kid-complete-modal").forEach((modal) => {
    const form = modal.querySelector("form");
    const cameraOpenButton = modal.querySelector("[data-camera-open]");
    const cameraInput = modal.querySelector("[data-camera-input]");
    const galleryInput = modal.querySelector("[data-gallery-input]");
    const cameraPanel = modal.querySelector("[data-camera-panel]");
    const cameraVideo = modal.querySelector("[data-camera-video]");
    const cameraCanvas = modal.querySelector("[data-camera-canvas]");
    const cameraCaptureButton = modal.querySelector("[data-camera-capture]");
    const cameraCancelButton = modal.querySelector("[data-camera-cancel]");
    const preview = modal.querySelector("[data-photo-preview]");
    let cameraStream = null;
    let previewUrls = [];

    const stopCamera = () => {
      cameraStream?.getTracks().forEach((track) => track.stop());
      cameraStream = null;
      if (cameraVideo) {
        cameraVideo.srcObject = null;
      }
      if (cameraPanel) {
        cameraPanel.hidden = true;
      }
    };

    const renderSelectedPhotos = () => {
      previewUrls.forEach((url) => URL.revokeObjectURL(url));
      previewUrls = [];
      if (!preview) {
        return;
      }

      preview.replaceChildren();
      const files = [
        ...Array.from(cameraInput?.files || []),
        ...Array.from(galleryInput?.files || [])
      ];
      files.slice(0, 8).forEach((file) => {
        const image = document.createElement("img");
        const url = URL.createObjectURL(file);
        previewUrls.push(url);
        image.src = url;
        image.alt = file.name;
        preview.appendChild(image);
      });
    };

    cameraOpenButton?.addEventListener("click", async () => {
      if (!navigator.mediaDevices?.getUserMedia || !cameraVideo || !cameraPanel) {
        cameraInput?.click();
        return;
      }

      try {
        cameraStream = await navigator.mediaDevices.getUserMedia({
          video: { facingMode: { ideal: "environment" } },
          audio: false
        });
        cameraVideo.srcObject = cameraStream;
        cameraPanel.hidden = false;
        await cameraVideo.play();
      } catch {
        cameraInput?.click();
      }
    });

    cameraCaptureButton?.addEventListener("click", () => {
      if (!cameraVideo || !cameraCanvas || !cameraInput ||
          cameraVideo.videoWidth === 0 || cameraVideo.videoHeight === 0) {
        return;
      }

      cameraCanvas.width = cameraVideo.videoWidth;
      cameraCanvas.height = cameraVideo.videoHeight;
      cameraCanvas.getContext("2d")?.drawImage(
        cameraVideo, 0, 0, cameraCanvas.width, cameraCanvas.height);
      cameraCanvas.toBlob((blob) => {
        if (!blob) {
          return;
        }

        const transfer = new DataTransfer();
        transfer.items.add(new File(
          [blob],
          `hamsterhub-${Date.now()}.jpg`,
          { type: "image/jpeg" }));
        cameraInput.files = transfer.files;
        renderSelectedPhotos();
        stopCamera();
      }, "image/jpeg", 0.9);
    });

    cameraCancelButton?.addEventListener("click", stopCamera);
    cameraInput?.addEventListener("change", renderSelectedPhotos);
    galleryInput?.addEventListener("change", renderSelectedPhotos);
    modal.addEventListener("hidden.bs.modal", () => {
      stopCamera();
      form?.reset();
      renderSelectedPhotos();
    });
  });

  document.querySelectorAll("[data-direct-reward-form]").forEach((form) => {
    const rewardSelect = form.querySelector("[data-direct-reward-reward]");
    const childSelect = form.querySelector("[data-direct-reward-child]");
    const costOutput = form.querySelector("[data-direct-reward-cost]");
    const balanceOutput = form.querySelector("[data-direct-reward-balance]");
    const remainingOutput = form.querySelector("[data-direct-reward-remaining]");
    const summary = form.querySelector("[data-direct-reward-summary]");
    const warning = form.querySelector("[data-direct-reward-warning]");
    const submitButton = form.querySelector("[data-direct-reward-submit]");

    const renderRewardSummary = () => {
      const rewardOption = rewardSelect?.selectedOptions[0];
      const childOption = childSelect?.selectedOptions[0];
      const hasReward = Boolean(rewardSelect?.value && rewardOption?.dataset.pointsCost);
      const hasChild = Boolean(childSelect?.value && childOption?.dataset.currentPoints);
      const cost = hasReward ? Number(rewardOption.dataset.pointsCost) : null;
      const balance = hasChild ? Number(childOption.dataset.currentPoints) : null;
      const remaining = cost !== null && balance !== null ? balance - cost : null;
      const isInsufficient = remaining !== null && remaining < 0;

      if (costOutput) {
        costOutput.textContent = cost ?? "—";
      }
      if (balanceOutput) {
        balanceOutput.textContent = balance ?? "—";
      }
      if (remainingOutput) {
        remainingOutput.textContent = remaining ?? "—";
      }
      summary?.classList.toggle("is-insufficient", isInsufficient);
      if (warning) {
        warning.hidden = !isInsufficient;
      }
      if (submitButton) {
        submitButton.disabled = !hasReward || !hasChild || isInsufficient;
      }
    };

    rewardSelect?.addEventListener("change", renderRewardSummary);
    childSelect?.addEventListener("change", renderRewardSummary);
    renderRewardSummary();
  });
});
