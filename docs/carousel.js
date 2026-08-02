document.addEventListener("DOMContentLoaded", () => {
    const track = document.querySelector('.carousel-track');
    const slides = Array.from(track.children);
    const dotsNav = document.querySelector('.carousel-controls');
    const dots = Array.from(dotsNav.children);
    const prevBtn = document.querySelector('.prev-btn');
    const nextBtn = document.querySelector('.next-btn');
    
    let currentIndex = 0;
    const slideDuration = 5000; // 5 seconds per slide
    let autoSlideInterval;

    const moveToSlide = (index) => {
        if(index < 0 || index >= slides.length) return;
        track.style.transform = 'translateX(-' + (index * 100) + '%)';
        dots[currentIndex].classList.remove('active');
        dots[index].classList.add('active');
        currentIndex = index;
    };

    const nextSlide = () => {
        let targetIndex = currentIndex + 1;
        if (targetIndex >= slides.length) {
            targetIndex = 0; // wrap around
        }
        moveToSlide(targetIndex);
    };

    const prevSlide = () => {
        let targetIndex = currentIndex - 1;
        if (targetIndex < 0) {
            targetIndex = slides.length - 1; // wrap around
        }
        moveToSlide(targetIndex);
    };

    const startAutoSlide = () => {
        autoSlideInterval = setInterval(nextSlide, slideDuration);
    };

    const stopAutoSlide = () => {
        clearInterval(autoSlideInterval);
    };

    // Click events for dots
    dots.forEach((dot, index) => {
        dot.addEventListener('click', () => {
            moveToSlide(index);
            stopAutoSlide(); // Pause auto slideshow on manual interaction
        });
    });

    // Click events for prev/next buttons
    if (prevBtn) {
        prevBtn.addEventListener('click', () => {
            prevSlide();
            stopAutoSlide(); // Pause auto slideshow on manual interaction
        });
    }

    if (nextBtn) {
        nextBtn.addEventListener('click', () => {
            nextSlide();
            stopAutoSlide(); // Pause auto slideshow on manual interaction
        });
    }

    // Start auto slide
    startAutoSlide();
});
