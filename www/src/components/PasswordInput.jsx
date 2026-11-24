import { useState } from 'react';
import strings from '../strings.js';
import Icon from './Icon.jsx';

/** Password input with a reveal toggle. Extra props go to the input. */
function PasswordInput({ id, value, onChange, autoComplete = 'current-password', invalid = false, describedBy, ...rest }) {
  const [reveal, setReveal] = useState(false);
  const label = reveal ? strings.auth.hidePassword : strings.auth.showPassword;
  return (
    <div className="input-with-button">
      <input
        id={id}
        type={reveal ? 'text' : 'password'}
        value={value}
        onChange={onChange}
        autoComplete={autoComplete}
        aria-invalid={invalid}
        aria-describedby={describedBy}
        spellCheck="false"
        {...rest}
      />
      <button type="button" className="icon-button" onClick={() => setReveal((v) => !v)} aria-label={label} title={label}>
        <Icon name={reveal ? 'eyeOff' : 'eye'} />
      </button>
    </div>
  );
}

export default PasswordInput;
