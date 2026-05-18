(function () {
    const phoneInputs = document.querySelectorAll('.js-phone-input');
    if (!phoneInputs.length || typeof window.intlTelInput !== 'function') {
        return;
    }

    const setValidationMessage = function (input, message) {
        const form = input.closest('form');
        const fieldName = input.getAttribute('name');
        const validationNode = form && fieldName
            ? form.querySelector(`[data-valmsg-for="${fieldName}"]`)
            : null;

        if (validationNode) {
            validationNode.textContent = message;
            validationNode.classList.toggle('field-validation-error', Boolean(message));
            validationNode.classList.toggle('field-validation-valid', !message);
        }

        input.setCustomValidity(message);
    };

    phoneInputs.forEach(function (input) {
        const form = input.closest('form');
        const countryInput = form ? form.querySelector('.js-phone-country') : null;
        const required = input.dataset.phoneRequired === 'true';
        const requiredMessage = input.dataset.phoneErrorRequired || 'Phone number is required.';
        const invalidMessage = input.dataset.phoneErrorInvalid || 'Please enter a valid phone number.';

        const iti = window.intlTelInput(input, {
            initialCountry: (countryInput && countryInput.value ? countryInput.value : 'EG').toLowerCase(),
            nationalMode: false,
            autoPlaceholder: 'aggressive',
            formatAsYouType: true,
            loadUtils: function () {
                return import('https://cdn.jsdelivr.net/npm/intl-tel-input@28.0.4/build/js/utils.js');
            }
        });

        const syncCountry = function () {
            const selectedCountry = iti.getSelectedCountryData();
            if (countryInput && selectedCountry && selectedCountry.iso2) {
                countryInput.value = selectedCountry.iso2.toUpperCase();
            }
        };

        const validate = function () {
            const value = input.value.trim();
            syncCountry();

            if (!value) {
                setValidationMessage(input, required ? requiredMessage : '');
                return !required;
            }

            if (!iti.isValidNumber()) {
                setValidationMessage(input, invalidMessage);
                return false;
            }

            input.value = iti.getNumber();
            setValidationMessage(input, '');
            return true;
        };

        input.addEventListener('countrychange', function () {
            syncCountry();
            if (input.value.trim()) {
                validate();
            }
        });

        input.addEventListener('input', function () {
            if (input.validationMessage) {
                validate();
            }
        });

        input.addEventListener('blur', validate);

        if (form) {
            form.addEventListener('submit', function (event) {
                if (!validate()) {
                    event.preventDefault();
                    event.stopPropagation();
                    input.reportValidity();
                }
            });
        }

        syncCountry();
    });
})();
