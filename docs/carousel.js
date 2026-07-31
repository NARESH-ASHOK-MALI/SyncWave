document.addEventListener("DOMContentLoaded", () => {
    const track = document.querySelector('.carousel-track');
    const slides = Array.from(track.children);
    const dotsNav = document.querySelector('.carousel-controls');
    const dots = Array.from(dotsNav.children);
    
    let currentIndex = 0;
    const slideDuration = 4000; // 4 seconds per slide
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

    const startAutoSlide = () => {
        autoSlideInterval = setInterval(nextSlide, slideDuration);
    };

    const resetAutoSlide = () => {
        clearInterval(autoSlideInterval);
        startAutoSlide();
    };

    // Click events for dots
    dots.forEach((dot, index) => {
        dot.addEventListener('click', () => {
            moveToSlide(index);
            resetAutoSlide();
        });
    });

    // Start auto slide
    startAutoSlide();
});
