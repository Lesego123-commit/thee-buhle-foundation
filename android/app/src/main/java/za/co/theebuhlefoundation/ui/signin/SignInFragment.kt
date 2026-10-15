// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.signin

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
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.databinding.FragmentSignInBinding
import za.co.theebuhlefoundation.ui.MainActivity
import za.co.theebuhlefoundation.ui.activate.ActivateFragment
import za.co.theebuhlefoundation.ui.common.ContainerViewModelFactory
import za.co.theebuhlefoundation.ui.common.container

class SignInFragment : Fragment() {

    private var _binding: FragmentSignInBinding? = null
    private val binding get() = _binding!!

    private val viewModel: SignInViewModel by viewModels {
        ContainerViewModelFactory(container) { SignInViewModel(it.auth) }
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        parent: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = FragmentSignInBinding.inflate(inflater, parent, false)

        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        binding.haveCodeButton.setOnClickListener { findNavController().navigate(R.id.toActivate) }

        // Back from "I have a code" with the password set: fill in the address
        // and say so. The password is typed again here, which doubles as the
        // check that they remember what they chose.
        findNavController().currentBackStackEntry?.savedStateHandle
            ?.getLiveData<String>(ActivateFragment.ACTIVATED_EMAIL)
            ?.observe(viewLifecycleOwner) { email ->
                if (email.isNullOrBlank()) return@observe

                binding.emailInput.setText(email)
                binding.passwordInput.requestFocus()
                Snackbar.make(binding.root, R.string.sign_in_activated, Snackbar.LENGTH_LONG).show()
                findNavController().currentBackStackEntry?.savedStateHandle?.remove<String>(ActivateFragment.ACTIVATED_EMAIL)
            }

        binding.signInButton.setOnClickListener {
            viewModel.signIn(
                binding.emailInput.text?.toString().orEmpty(),
                binding.passwordInput.text?.toString().orEmpty(),
            )
        }

        // repeatOnLifecycle rather than a bare collect: collection stops when
        // the fragment is not visible, so a response arriving while the user is
        // in another app does not touch views that are no longer there.
        viewLifecycleOwner.lifecycleScope.launch {
            viewLifecycleOwner.repeatOnLifecycle(Lifecycle.State.STARTED) {
                viewModel.state.collect { render(it) }
            }
        }
    }

    private fun render(state: SignInState) {
        binding.signInButton.isEnabled = !state.busy
        binding.progress.visibility = if (state.busy) View.VISIBLE else View.GONE

        binding.emailField.error = state.emailError
        binding.passwordField.error = state.passwordError

        state.message?.let {
            Snackbar.make(binding.root, it, Snackbar.LENGTH_LONG).show()
            viewModel.messageShown()
        }

        if (state.signedIn) {
            (requireActivity() as MainActivity).applyMenuForRole()

            findNavController().navigate(
                R.id.homeFragment,
                null,
                androidx.navigation.navOptions {
                    // The sign-in screen is removed from the back stack, so the
                    // back button from Home leaves the app rather than
                    // returning to a form that would sign somebody else in.
                    popUpTo(R.id.signInFragment) { inclusive = true }
                },
            )
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
