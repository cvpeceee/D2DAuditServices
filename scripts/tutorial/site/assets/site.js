/* D2D Audit tutorials site: renders the player and walkthrough from media/tutorial-chapters.json. */
(function () {
  "use strict";
  var MANIFEST = "media/tutorial-chapters.json";

  function fmt(t) {
    t = Math.max(0, Math.round(t));
    return Math.floor(t / 60) + ":" + String(t % 60).padStart(2, "0");
  }
  function el(tag, attrs, children) {
    var node = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      if (k === "text") node.textContent = attrs[k];
      else if (k.indexOf("on") === 0) node.addEventListener(k.slice(2), attrs[k]);
      else node.setAttribute(k, attrs[k]);
    });
    (children || []).forEach(function (c) {
      if (c) node.appendChild(c);
    });
    return node;
  }
  function load() {
    return fetch(MANIFEST, { cache: "no-cache" }).then(function (r) {
      if (!r.ok) throw new Error("manifest " + r.status);
      return r.json();
    });
  }
  function deepLink(m, route) {
    var base = (m.liveDemo || "https://d2d-audit-web.onrender.com/").replace(/\/?$/, "/");
    return !route || route === "/login" ? base : base + "#" + route;
  }

  function renderPlayer() {
    var video = document.getElementById("video");
    var list = document.getElementById("chapter-list");
    load()
      .then(function (m) {
        document.getElementById("title").textContent = m.title || document.title;
        document.getElementById("meta").textContent =
          fmt(m.totalDurationSeconds) + " · " + m.resolution + " · captions on by default";
        var buttons = m.chapters.map(function (c) {
          var b = el("button", {
            type: "button",
            onclick: function () {
              video.currentTime = c.startTime + 0.05;
              video.play();
            },
          }, [
            el("span", {}, [el("b", { text: c.number + ". " + c.title }), el("small", { text: c.summary || "" })]),
            el("span", { class: "kbd", text: fmt(c.startTime) }),
          ]);
          list.appendChild(el("li", {}, [b]));
          return b;
        });
        video.addEventListener("timeupdate", function () {
          m.chapters.forEach(function (c, i) {
            var on = video.currentTime >= c.startTime && video.currentTime < c.endTime;
            buttons[i].setAttribute("aria-current", on ? "true" : "false");
          });
        });
        var clips = document.getElementById("clips");
        m.chapters.forEach(function (c) {
          if (!c.clip) return;
          clips.appendChild(
            el("div", { class: "card" }, [
              el("video", { src: "media/" + c.clip, controls: "", preload: "none", poster: c.poster ? "media/" + c.poster : "media/poster.jpg" }),
              el("div", { class: "body" }, [
                el("h3", { text: c.title }),
                el("p", { text: c.summary + " (" + fmt(c.duration) + ")" }),
              ]),
            ]),
          );
        });
      })
      .catch(function (e) {
        list.appendChild(el("li", { text: "Chapter list unavailable (" + e.message + ")." }));
      });
  }

  function renderWalkthrough() {
    var stepsNav = document.getElementById("steps");
    var stepBox = document.getElementById("step");
    var bar = document.getElementById("progress-bar");
    load()
      .then(function (m) {
        var chapterTitle = {};
        m.chapters.forEach(function (c) {
          chapterTitle[c.id] = c.title;
        });
        var steps = (m.scenes || []).filter(function (s) {
          return s.still;
        });
        var buttons = [];
        var lastChapter = null;
        steps.forEach(function (s, i) {
          if (s.chapter !== lastChapter) {
            stepsNav.appendChild(el("div", { class: "chap", text: chapterTitle[s.chapter] || s.chapter }));
            lastChapter = s.chapter;
          }
          var b = el("button", { type: "button", text: i + 1 + ". " + s.title, onclick: function () { show(i); } });
          buttons.push(b);
          stepsNav.appendChild(b);
        });
        var current = 0;
        function show(i) {
          current = Math.max(0, Math.min(steps.length - 1, i));
          var s = steps[current];
          buttons.forEach(function (b, j) {
            b.setAttribute("aria-current", j === current ? "true" : "false");
          });
          bar.style.width = ((current + 1) / steps.length) * 100 + "%";
          stepBox.innerHTML = "";
          stepBox.appendChild(el("h2", { text: "Step " + (current + 1) + " of " + steps.length + ": " + s.title }));
          stepBox.appendChild(el("img", { src: "media/" + s.still, alt: s.title + " screen", loading: "lazy" }));
          stepBox.appendChild(el("p", { class: "narr", text: s.narration }));
          stepBox.appendChild(
            el("div", { class: "row" }, [
              el("button", { type: "button", text: "← Back", onclick: function () { show(current - 1); } }),
              el("button", { type: "button", text: "Next →", onclick: function () { show(current + 1); } }),
              el("a", { class: "cta", href: deepLink(m, s.route), target: "_blank", rel: "noopener", text: "Open this screen in the live demo" }),
              el("a", { href: "./#t=" + s.startTime, text: "Watch this moment (" + fmt(s.startTime) + ")" }),
            ]),
          );
        }
        document.addEventListener("keydown", function (e) {
          if (e.key === "ArrowRight") show(current + 1);
          if (e.key === "ArrowLeft") show(current - 1);
        });
        if (steps.length) show(0);
      })
      .catch(function (e) {
        stepBox.textContent = "Walkthrough unavailable (" + e.message + ").";
      });
  }

  // Support index.html#t=SECONDS deep links from the walkthrough.
  window.addEventListener("load", function () {
    var v = document.getElementById("video");
    var match = /#t=([\d.]+)/.exec(location.hash);
    if (v && match) {
      var seek = function () {
        v.currentTime = parseFloat(match[1]);
      };
      if (v.readyState >= 1) seek();
      else v.addEventListener("loadedmetadata", seek, { once: true });
    }
  });

  window.D2DTutorials = { renderPlayer: renderPlayer, renderWalkthrough: renderWalkthrough };
})();
