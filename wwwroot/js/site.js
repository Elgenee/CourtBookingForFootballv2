// Court Booking System — site.js
// Lightweight client-side helpers. Most interactive behavior is provided by AdminLTE/Bootstrap.

(function ($) {
    "use strict";

    // Smooth scroll for in-page anchor links.
    $(document).on("click", 'a[href^="#"]', function (e) {
        var target = $(this).attr("href");
        if (target.length > 1 && $(target).length) {
            e.preventDefault();
            $("html, body").animate({ scrollTop: $(target).offset().top - 60 }, 350);
        }
    });

    // Add 'shrunk' class to navbar on scroll for subtle depth (optional cosmetic).
    $(window).on("scroll", function () {
        if ($(window).scrollTop() > 30) {
            $(".main-header").addClass("shadow-sm");
        } else {
            $(".main-header").removeClass("shadow-sm");
        }
    });

    // Payment proof lightbox modal — populates image/src/ref on open.
    $(document).on("show.bs.modal", "#proofModal", function (event) {
        var trigger = $(event.relatedTarget);
        var src = trigger.attr("data-src") || trigger.data("src");
        var ref = trigger.attr("data-ref") || trigger.data("ref") || "—";
        var modal = $(this);
        modal.find("#proofModalImage").attr("src", src);
        modal.find("#proofModalRef").text(ref);
        modal.find("#proofModalOpen").attr("href", src);
    });

})(jQuery);
