// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.activate

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.fragment.app.viewModels
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.navigation.fragment.findNavController
import com.google.android.material.snackbar.Snackbar
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.databinding.FragmentActivateBinding
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.container

/**
 * Sets a password with a code from the foundation, then goes back to sign-in
 * with the email address already filled in.
 */
class ActivateFragment : Fragment() {

    private var _binding: FragmentActivateBinding? = null
    private val binding get() = _binding!!

    private val viewModel: ActivateViewModel by viewModels {
        ContainerViewModelFactory(container) { ActivateViewModel(it.auth) }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentActivateBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        binding.activateButton.setOnClickListener {
            viewModel.submit(
                binding.emailInput.text?.toString().orEmpty(),
                binding.codeInput.text?.toString().orEmpty(),
                binding.passwordInput.text?.toString().orEmpty(),
                binding.confirmInput.text?.toString().orEmpty(),
            )
        }

        binding.backToSignIn.setOnClickListener { findNavController().popBackStack() }

        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { render(it) }
            }
        }
    }

    private fun render(state: ActivateState) {
        binding.activateButton.isEnabled = !state.busy
        binding.progress.visibility = if (state.busy) View.VISIBLE else View.GONE

        binding.emailField.error = state.emailError
        binding.codeField.error = state.codeError
        binding.passwordField.error = state.passwordError
        binding.confirmField.error = state.confirmError

        state.message?.let {
            Snackbar.make(binding.root, it, Snackbar.LENGTH_LONG).show()
            viewModel.messageShown()
        }

        state.doneFor?.let { email ->
            // Handed back to the sign-in screen, which fills in the address and
            // says the password is set. The password itself is not passed on:
            // typing it once more is the check that they remember it.
            findNavController().previousBackStackEntry?.savedStateHandle?.set(ACTIVATED_EMAIL, email)
            findNavController().popBackStack()
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    companion object {
        const val ACTIVATED_EMAIL = "activatedEmail"
    }
}
